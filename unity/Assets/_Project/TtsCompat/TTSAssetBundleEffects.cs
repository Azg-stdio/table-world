using System;
using System.Collections;
using System.Collections.Generic;
using TableWorld.View;
using UnityEngine;

// ADR 0008. A stand-in with the identity of TTS's effects component (class TTSAssetBundleEffects, no namespace,
// Assembly-CSharp): when a TTS asset bundle loads, Unity binds the component in it to this class and fills these fields
// from the bundle. Only the serialized field names and types are re-declared, from the layout stored in the bundles;
// nothing of TTS's code is copied. This file lives outside every asmdef on purpose and must never be renamed or moved
// into a namespace.

/// <summary>TTS's effects component, as Table World reads and plays it.</summary>
[DisallowMultipleComponent]
public class TTSAssetBundleEffects : MonoBehaviour, ITtsBundleEffects
{
    public List<TTSLoopingEffect> LoopingEffects = new List<TTSLoopingEffect>();
    public List<TTSTriggerEffect> TriggerEffects = new List<TTSTriggerEffect>();

    private int _looping = -1;
    private Coroutine _trigger;
    private AudioSource _audio;

    public int LoopingCount => LoopingEffects?.Count ?? 0;
    public int TriggerCount => TriggerEffects?.Count ?? 0;
    public string LoopingName(int index) => index >= 0 && index < LoopingCount ? LoopingEffects[index].Name ?? "" : "";
    public string TriggerName(int index) => index >= 0 && index < TriggerCount ? TriggerEffects[index].Name ?? "" : "";

    public void PlayLooping(int index)
    {
        if (index < 0 || index >= LoopingCount)
            return;
        if (_trigger != null)
        {
            StopCoroutine(_trigger);
            _trigger = null;
        }
        _looping = index;
        Show(LoopingEffects[index], loop: true);
    }

    public float PlayTrigger(int index)
    {
        if (index < 0 || index >= TriggerCount)
            return 0f;
        var effect = TriggerEffects[index];
        if (_trigger != null)
            StopCoroutine(_trigger);
        Show(effect, loop: false);
        var seconds = effect.Duration > 0f ? effect.Duration : ClipLength(effect.Animation);
        _trigger = StartCoroutine(BackToLooping(seconds));
        return seconds;
    }

    private IEnumerator BackToLooping(float seconds)
    {
        yield return new WaitForSeconds(Mathf.Max(0.05f, seconds));
        _trigger = null;
        if (_looping >= 0)
            Show(LoopingEffects[_looping], loop: true);
    }

    /// <summary>Hides every effect's own objects, then shows this one's: its objects, particles, animation and sound.</summary>
    private void Show(TTSLoopingEffect effect, bool loop)
    {
        foreach (var other in AllEffects())
            if (other != effect)
                SetObjects(other, false);
        SetObjects(effect, true);
        foreach (var particles in effect.Particles ?? new List<ParticleSystem>())
            if (particles != null)
                particles.Play(true);
        if (effect.Animation?.AnimationComponent is { } animation && !string.IsNullOrEmpty(effect.Animation.AnimationName)
            && animation[effect.Animation.AnimationName] is { } state)
        {
            state.wrapMode = loop ? WrapMode.Loop : WrapMode.Once;
            animation.Play(effect.Animation.AnimationName);
        }
        if (effect.Animator?.AnimatorComponent is { } animator && !string.IsNullOrEmpty(effect.Animator.StateName))
            animator.Play(effect.Animator.StateName);
        if (effect.Sound?.Audio != null)
        {
            if (_audio == null)
                _audio = gameObject.AddComponent<AudioSource>();
            _audio.clip = effect.Sound.Audio;
            _audio.loop = loop;
            _audio.spatialBlend = effect.Sound.Positional3D ? 1f : 0f;
            _audio.Play();
        }
    }

    private IEnumerable<TTSLoopingEffect> AllEffects()
    {
        foreach (var e in LoopingEffects ?? new List<TTSLoopingEffect>())
            yield return e;
        foreach (var e in TriggerEffects ?? new List<TTSTriggerEffect>())
            yield return e;
    }

    private static void SetObjects(TTSLoopingEffect effect, bool active)
    {
        foreach (var go in effect.GameObjects ?? new List<GameObject>())
            if (go != null)
                go.SetActive(active);
    }

    private static float ClipLength(TTSEffectAnimation animation) =>
        animation?.AnimationComponent is { } a && !string.IsNullOrEmpty(animation.AnimationName) && a[animation.AnimationName] is { } s ? s.length : 1f;
}

[Serializable]
public class TTSLoopingEffect
{
    public string Name;
    public List<GameObject> GameObjects = new List<GameObject>();
    public List<ParticleSystem> Particles = new List<ParticleSystem>();
    public TTSEffectSound Sound = new TTSEffectSound();
    public TTSEffectAnimation Animation = new TTSEffectAnimation();
    public TTSEffectAnimator Animator = new TTSEffectAnimator();
}

[Serializable]
public class TTSTriggerEffect : TTSLoopingEffect
{
    public float Duration;
}

[Serializable]
public class TTSEffectSound
{
    public AudioClip Audio;
    public bool Positional3D = true;
}

[Serializable]
public class TTSEffectAnimation
{
    public Animation AnimationComponent;
    public string AnimationName;
}

[Serializable]
public class TTSEffectAnimator
{
    public Animator AnimatorComponent;
    public string StateName;
}
