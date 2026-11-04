using System;
using System.Collections;
using System.Collections.Generic;
using GameFramework.Resource;
using UnityEngine;

namespace GameFramework.Audio
{
    /// <summary>音量分组。层级：Master × Group × 单次播放音量。</summary>
    public enum AudioGroup
    {
        Master = 0,
        Bgm = 1,
        Sfx = 2,
        Voice = 3,
    }

    /// <summary>
    /// 音频服务。
    ///
    /// 设计取舍（参考了 GameFramework 的 Sound 模块，但按我们的资源层重写）：
    ///   · **不依赖 AudioMixer**：mixer 是个 .asset，会把"能跑"变成"必须先配资源"。
    ///     这里用三层音量相乘（master × group × clip）+ PlayerPrefs 持久化，
    ///     将来要接 mixer 只需改 `ApplyVolume` 一处。
    ///   · **双 BGM source 交叉淡入**：单 source 直接换 clip 会爆音（前一首被硬切）。
    ///   · **SFX 用固定 source 池轮询**：不用 PlayOneShot —— PlayOneShot 无法单独控音量组，
    ///     也无法在"音效数量爆炸"时做丢弃。
    ///   · 资源走 <see cref="ResourceService"/> 异步加载并**常驻**（音频 clip 太小，反复加载不划算）。
    ///
    /// ★ 播放接口全是"传 location"而不是传 AudioClip：业务不该关心加载。
    /// </summary>
    public class AudioService
    {
        private const string KeyMaster = "audio.master";
        private const string KeyBgm = "audio.bgm";
        private const string KeySfx = "audio.sfx";
        private const string KeyVoice = "audio.voice";

        private ResourceService _res;
        private Transform _root;

        private AudioSource _bgmA;
        private AudioSource _bgmB;
        private bool _aIsActive = true;
        private Coroutine _bgmFade;

        private AudioSource[] _sfx;
        private AudioGroup[] _sfxGroups;   // 每个通道当前播放的组，决定音量取哪一档
        private int _sfxCursor;

        private readonly Dictionary<string, AudioClip> _clipCache = new Dictionary<string, AudioClip>(32);
        private readonly Dictionary<AudioGroup, float> _volumes = new Dictionary<AudioGroup, float>(4);

        /// <summary>BGM 当前的 location（用于"同一首不重播"判定）</summary>
        public string CurrentBgm { get; private set; }
        public bool IsMuted { get; private set; }

        private bool _inited;

        // ---------------- 生命周期 ----------------

        public void Init(ResourceService resource, int sfxVoices = 8)
        {
            if (_inited) return;
            _res = resource;

            var go = new GameObject("[Audio]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _root = go.transform;

            _bgmA = CreateSource("BgmA", true);
            _bgmB = CreateSource("BgmB", true);
            _bgmA.volume = 0f;
            _bgmB.volume = 0f;

            if (sfxVoices < 2) sfxVoices = 2;
            _sfx = new AudioSource[sfxVoices];
            _sfxGroups = new AudioGroup[sfxVoices];
            for (int i = 0; i < sfxVoices; i++)
            {
                _sfx[i] = CreateSource("Sfx" + i, false);
                _sfxGroups[i] = AudioGroup.Sfx;
            }

            LoadVolumes();
            _inited = true;
            GameLog.Info("[Audio] 已初始化，SFX 通道 " + sfxVoices);
        }

        public void Dispose()
        {
            if (!_inited) return;
            StopBgm(0f);
            if (_root != null) UnityEngine.Object.Destroy(_root.gameObject);
            _clipCache.Clear();
            _inited = false;
        }

        private AudioSource CreateSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_root, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.spatialBlend = 0f;      // 2D
            src.volume = 0f;
            return src;
        }

        // ---------------- BGM ----------------

        /// <summary>
        /// 播放背景音乐（同一首正在播则忽略）。
        /// fadeSeconds = 0 表示硬切。
        /// </summary>
        public void PlayBgm(string location, float fadeSeconds = 0.8f, float volume = 1f, bool loop = true)
        {
            if (!_inited || string.IsNullOrEmpty(location)) return;
            if (CurrentBgm == location && IsBgmPlaying()) return;

            CurrentBgm = location;

            _res.LoadAsset<AudioClip>(location, delegate (AudioClip clip)
            {
                if (clip == null) return;
                // 加载期间玩家可能又切了别的 BGM，丢弃过期请求
                if (CurrentBgm != location) return;

                var next = _aIsActive ? _bgmB : _bgmA;
                var prev = _aIsActive ? _bgmA : _bgmB;

                next.clip = clip;
                next.loop = loop;
                next.volume = 0f;
                next.Play();

                StartFade(prev, next, fadeSeconds, volume);
                _aIsActive = !_aIsActive;
            },
            delegate (string err) { GameLog.Warn("[Audio] BGM 加载失败: " + err); });
        }

        public void StopBgm(float fadeSeconds = 0.6f)
        {
            if (!_inited) return;
            CurrentBgm = null;

            var cur = _aIsActive ? _bgmA : _bgmB;
            StartFade(cur, null, fadeSeconds, 1f);
        }

        public bool IsBgmPlaying()
        {
            if (!_inited) return false;
            var cur = _aIsActive ? _bgmA : _bgmB;
            return cur != null && cur.isPlaying;
        }

        private void StartFade(AudioSource from, AudioSource to, float duration, float targetVolume)
        {
            var loop = GameLoop.Ensure();
            if (_bgmFade != null) loop.StopRun(_bgmFade);
            _bgmFade = loop.Run(FadeRoutine(from, to, duration, targetVolume));
        }

        private IEnumerator FadeRoutine(AudioSource from, AudioSource to, float duration, float targetVolume)
        {
            float fromStart = from != null ? from.volume : 0f;
            float toTarget = to != null ? targetVolume * EffectiveVolume(AudioGroup.Bgm) : 0f;

            if (duration <= 0f)
            {
                if (from != null) { from.volume = 0f; from.Stop(); }
                if (to != null) to.volume = toTarget;
                yield break;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;   // 用不受 timeScale 影响的时间：暂停时淡出也要走
                float k = Mathf.Clamp01(t / duration);

                if (from != null) from.volume = Mathf.Lerp(fromStart, 0f, k);
                if (to != null) to.volume = Mathf.Lerp(0f, toTarget, k);
                yield return null;
            }

            if (from != null) { from.volume = 0f; from.Stop(); from.clip = null; }
            if (to != null) to.volume = toTarget;
            _bgmFade = null;
        }

        // ---------------- SFX / Voice ----------------

        /// <summary>播放音效（异步加载 + 池轮询）。音量 = volume × Sfx 组音量 × master。</summary>
        public void PlaySfx(string location, float volume = 1f, float pitch = 1f)
        {
            PlayOneShotInternal(location, AudioGroup.Sfx, volume, pitch);
        }

        public void PlayVoice(string location, float volume = 1f)
        {
            PlayOneShotInternal(location, AudioGroup.Voice, volume, 1f);
        }

        private void PlayOneShotInternal(string location, AudioGroup group, float volume, float pitch)
        {
            if (!_inited || string.IsNullOrEmpty(location)) return;

            AudioClip cached;
            if (_clipCache.TryGetValue(location, out cached) && cached != null)
            {
                PlayClip(cached, group, volume, pitch);
                return;
            }

            _res.LoadAsset<AudioClip>(location, delegate (AudioClip clip)
            {
                if (clip == null) return;
                if (_clipCache.Count < 64) _clipCache[location] = clip;
                PlayClip(clip, group, volume, pitch);
            },
            delegate (string err) { GameLog.Warn("[Audio] 音效加载失败: " + err); });
        }

        private void PlayClip(AudioClip clip, AudioGroup group, float volume, float pitch)
        {
            if (_sfx == null || _sfx.Length == 0) return;

            // 先找空闲通道，全忙则抢占当前游标（丢最老的那个，避免音效堆积）
            AudioSource target = null;
            int slot = -1;
            for (int i = 0; i < _sfx.Length; i++)
            {
                int idx = (_sfxCursor + i) % _sfx.Length;
                if (!_sfx[idx].isPlaying) { target = _sfx[idx]; slot = idx; _sfxCursor = (idx + 1) % _sfx.Length; break; }
            }
            if (target == null)
            {
                slot = _sfxCursor;
                target = _sfx[_sfxCursor];
                _sfxCursor = (_sfxCursor + 1) % _sfx.Length;
            }

            _sfxGroups[slot] = group;
            target.clip = clip;
            target.pitch = Mathf.Clamp(pitch, 0.5f, 2f);
            target.volume = Mathf.Clamp01(volume) * EffectiveVolume(group);
            target.Play();
        }

        // ---------------- 音量 ----------------

        public float GetVolume(AudioGroup group)
        {
            float v;
            return _volumes.TryGetValue(group, out v) ? v : 1f;
        }

        public void SetVolume(AudioGroup group, float volume)
        {
            volume = Mathf.Clamp01(volume);
            _volumes[group] = volume;
            SaveVolume(group, volume);
            ApplyVolume();
        }

        public void Mute(bool mute)
        {
            IsMuted = mute;
            ApplyVolume();
        }

        /// <summary>某组当前实际生效的音量（含 master 与静音）</summary>
        public float EffectiveVolume(AudioGroup group)
        {
            if (IsMuted) return 0f;
            float master = GetVolume(AudioGroup.Master);
            if (group == AudioGroup.Master) return master;
            return master * GetVolume(group);
        }

        private void ApplyVolume()
        {
            if (!_inited) return;

            if (_bgmA != null && _bgmA.isPlaying) _bgmA.volume = EffectiveVolume(AudioGroup.Bgm);
            if (_bgmB != null && _bgmB.isPlaying) _bgmB.volume = EffectiveVolume(AudioGroup.Bgm);

            for (int i = 0; i < _sfx.Length; i++)
            {
                if (_sfx[i].isPlaying)
                    _sfx[i].volume = EffectiveVolume(_sfxGroups[i]);
            }
        }

        private void LoadVolumes()
        {
            _volumes[AudioGroup.Master] = PlayerPrefs.GetFloat(KeyMaster, 1f);
            _volumes[AudioGroup.Bgm] = PlayerPrefs.GetFloat(KeyBgm, 0.7f);
            _volumes[AudioGroup.Sfx] = PlayerPrefs.GetFloat(KeySfx, 1f);
            _volumes[AudioGroup.Voice] = PlayerPrefs.GetFloat(KeyVoice, 1f);
        }

        private static void SaveVolume(AudioGroup group, float v)
        {
            switch (group)
            {
                case AudioGroup.Master: PlayerPrefs.SetFloat(KeyMaster, v); break;
                case AudioGroup.Bgm: PlayerPrefs.SetFloat(KeyBgm, v); break;
                case AudioGroup.Sfx: PlayerPrefs.SetFloat(KeySfx, v); break;
                case AudioGroup.Voice: PlayerPrefs.SetFloat(KeyVoice, v); break;
            }
            PlayerPrefs.Save();
        }
    }
}
