using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SeniorParty
{
    public class SoundManager : MonoBehaviour
    {
        public static SoundManager Instance;

        // ========================
        // 변수
        // ========================

        [Header("Audio Mixer")]
        public AudioMixer AudioMixer;

        // MainMenuController의 슬라이더가 쓰는 값과 동일해야 함(믹서에 노출된 파라미터 이름).
        private const string MasterVolumeParam = "MasterVolume";
        private const string BgmVolumeParam = "BGMVolume";
        private const string SfxVolumeParam = "SFXVolume";

        // MainMenuController.SliderToDecibel / DefaultVolumeSlider와 동일한 매핑.
        private const float MinDb = -80f;
        private const float MaxBoostDb = 10f;
        private const float DefaultVolumeSlider = 80f;

        [Header("SFX Pool")]
        [SerializeField] private int _sfxPoolSize = 16;

        [System.Serializable]
        public struct ClipEntry
        {
            public string ClipName;
            public AudioClip Clip;
        }

        [Header("SFX Clips")]
        [SerializeField] private List<ClipEntry> _sfxEntries = new();

        [Header("BGM Clips")]
        [SerializeField] private List<ClipEntry> _bgmEntries = new();

        public Dictionary<string, AudioClip> SfxDict = new();
        public Dictionary<string, AudioClip> BgmDict = new();

        private AudioSource[] _bgmSources;
        private int _currentBGMIndex = 0;
        private Coroutine _crossfadeCoroutine;
        private AudioSource[] _sfxPool;
        private int _sfxPoolCursor = 0;

        // ========================
        // 유니티 라이프사이클
        // ========================

        private void Awake()
        {
            if (Instance != null)
            {
                // Instance가 이미 자기 자신이면(도메인/씬 리로드 없이 Play를 다시 눌러 Awake가 재실행된 경우) 재초기화 없이 그냥 둠 —
                // 안 그러면 아래 else 분기로 빠져 자기 자신을 파괴하고, 이후 죽은 AudioSource에 접근해 MissingReferenceException이 터짐
                if (Instance != this)
                {
                    Destroy(gameObject);
                }
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            InitializePools();
            InitializeDictionaries();
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            // MainMenuController가 설정 화면을 열기 전에도 재생되는 오디오가 저장된 볼륨을 따르도록 한 번 적용.
            ApplySavedVolumes(SaveManager.Load());
        }

        private void OnEnable()
        {
            PlayBGM("BGM1");
        }

        // ========================
        // 초기화
        // ========================
    
        // 인스펙터에 등록된 SFX/BGM 클립 리스트를 이름→클립 딕셔너리로 변환
        private void InitializeDictionaries()
        {
            foreach (ClipEntry entry in _sfxEntries)
            {
                if (entry.Clip == null)
                {
                    continue;
                }
                if (!SfxDict.TryAdd(entry.ClipName, entry.Clip))
                {
                    Debug.LogWarning($"[SoundManager] SFX 이름 중복: {entry.ClipName}");
                }
            }

            foreach (ClipEntry entry in _bgmEntries)
            {
                if (entry.Clip == null)
                {
                    continue;
                }
                if (!BgmDict.TryAdd(entry.ClipName, entry.Clip))
                {
                    Debug.LogWarning($"[SoundManager] BGM 이름 중복: {entry.ClipName}");
                }
            }
        }

        // BGM 크로스페이드용 AudioSource 2개 + SFX 재생용 오브젝트 풀 생성
        private void InitializePools()
        {
            _bgmSources = new AudioSource[2];
            for (int i = 0; i < 2; i++)
            {
                _bgmSources[i] = gameObject.AddComponent<AudioSource>();
                _bgmSources[i].outputAudioMixerGroup = AudioMixer.FindMatchingGroups("BGM")[0];
                // 반복은 PlayBGMLoop이 끝나기 직전에 크로스페이드로 처리하므로 네이티브 loop는 꺼둔다.
                _bgmSources[i].loop = false;
                _bgmSources[i].playOnAwake = false;
            }

            _sfxPool = new AudioSource[_sfxPoolSize];
            AudioMixerGroup sfxGroup = AudioMixer.FindMatchingGroups("SFX")[0];
            for (int i = 0; i < _sfxPoolSize; i++)
            {
                _sfxPool[i] = gameObject.AddComponent<AudioSource>();
                _sfxPool[i].outputAudioMixerGroup = sfxGroup;
                _sfxPool[i].playOnAwake = false;
            }
        }

        // ========================
        // SFX 재생
        // ========================

        // 이름으로 등록된 SFX 클립을 풀에서 하나 꺼내 재생 (매번 랜덤 피치)
        public void PlaySFX(string clipName)
        {
            PlaySFX(clipName, Random.Range(0.98f, 1.02f)); // 매번 살짝 다르게 들리도록 피치 배율을 ±2% 랜덤으로 확정
        }

        // 피치를 직접 지정해서 재생 (예: 점등마다 피치를 조금씩 올리는 연출)
        public void PlaySFX(string clipName, float pitch)
        {
            if (!SfxDict.TryGetValue(clipName, out AudioClip clip))
            {
                Debug.LogWarning($"[SoundManager] SFX 클립을 찾을 수 없음: {clipName}");
                return;
            }
            AudioSource src = GetPooledSFXSource();
            src.clip = clip;
            src.pitch = pitch;
            src.Play();
        }

        // 재생 중이 아닌 풀 슬롯을 순환 탐색해서 반환 (전부 재생 중이면 가장 오래된 것을 덮어씀)
        public AudioSource GetPooledSFXSource()
        {
            for (int i = 0; i < _sfxPoolSize; i++)
            {
                int idx = (_sfxPoolCursor + i) % _sfxPoolSize;
                if (!_sfxPool[idx].isPlaying)
                {
                    _sfxPoolCursor = (idx + 1) % _sfxPoolSize;
                    return _sfxPool[idx];
                }
            }
            AudioSource overwrite = _sfxPool[_sfxPoolCursor];
            _sfxPoolCursor = (_sfxPoolCursor + 1) % _sfxPoolSize;
            return overwrite;
        }

        // ========================
        // BGM 재생 / 크로스페이드
        // ========================

        // 이름으로 등록된 BGM을 처음부터 끝까지 재생하고, 끝나갈 때쯔음 CrossfadeBGM으로 다시 처음부터 틀어 반복
        public void PlayBGM(string clipName, float fadeDuration = 1f)
        {
            if (!BgmDict.TryGetValue(clipName, out AudioClip clip))
            {
                Debug.LogWarning($"[SoundManager] BGM 클립을 찾을 수 없음: {clipName}");
                return;
            }
            if (_crossfadeCoroutine != null)
            {
                StopCoroutine(_crossfadeCoroutine);
            }
            _crossfadeCoroutine = StartCoroutine(PlayBGMLoop(clip, fadeDuration));
        }

        // 한 번 크로스페이드로 틀고, 끝나기 fadeDuration 전에 같은 클립으로 다시 크로스페이드(반복).
        private IEnumerator PlayBGMLoop(AudioClip clip, float fadeDuration)
        {
            while (true)
            {
                yield return CrossfadeBGM(clip, fadeDuration);
                yield return new WaitUntil(() => _bgmSources[_currentBGMIndex].time >= clip.length - fadeDuration);
            }
        }

        // 안쓰는 쪽 AudioSource로 새 클립을 틀면서 두 소스의 볼륨을 교차시켜 자연스럽게 전환
        private IEnumerator CrossfadeBGM(AudioClip newClip, float duration, float startTime = 0f)
        {
            int next = 1 - _currentBGMIndex;

            _bgmSources[next].clip = newClip;
            _bgmSources[next].time = startTime;
            _bgmSources[next].volume = 0f;
            _bgmSources[next].pitch = 1f;
            _bgmSources[next].Play();

            float elapsed = 0f;
            float startVol = _bgmSources[_currentBGMIndex].volume;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(duration, 0.01f));
                _bgmSources[_currentBGMIndex].volume = Mathf.Lerp(startVol, 0f, t);
                _bgmSources[next].volume = Mathf.Lerp(0f, 1f, t);
                yield return null;
            }

            _bgmSources[_currentBGMIndex].Stop();
            _bgmSources[_currentBGMIndex].volume = 1f;
            _currentBGMIndex = next;
        }

        // ========================
        // 볼륨 (Mixer 연동)
        // ========================

        // 저장된 볼륨 값(0~100)을 믹서에 반영. 슬라이더 UI는 MainMenuController가 관리하므로 여기서는 믹서 적용만 담당.
        private void ApplySavedVolumes(SaveData s)
        {
            AudioMixer.SetFloat(MasterVolumeParam, SliderToDecibel(s.masterVolume));
            AudioMixer.SetFloat(BgmVolumeParam, SliderToDecibel(s.bgmVolume));
            AudioMixer.SetFloat(SfxVolumeParam, SliderToDecibel(s.sfxVolume));
        }

        private static float SliderToDecibel(float sliderValue)
        {
            if (sliderValue <= 0f)
            {
                return MinDb;
            }
            if (sliderValue >= DefaultVolumeSlider)
            {
                return (sliderValue - DefaultVolumeSlider) / (100f - DefaultVolumeSlider) * MaxBoostDb;
            }
            return Mathf.Clamp(20f * Mathf.Log10(sliderValue / DefaultVolumeSlider), MinDb, 0f);
        }
    }
}
