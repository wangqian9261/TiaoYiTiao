using UnityEngine;

/// <summary>Only local skeletal presentation. Gameplay owns position, flight and collision.</summary>
[DisallowMultipleComponent]
public sealed class JumpCharacterAnimation : MonoBehaviour
{
    [SerializeField] private Animator animator;
    private static readonly int ChargeProgress = Animator.StringToHash("ChargeProgress");
    private float _elapsed;
    private float _takeoffDuration = .16f;
    public string CurrentState { get; private set; } = "Idle";
    public const float CelebrateDuration = 2f;

    private void Awake()
    {
        EnsureAnimator();
        ResetIdle();
    }

    private bool EnsureAnimator()
    {
        if (animator == null) animator = GetComponentInChildren<Animator>(true);
        if (animator == null || animator.runtimeAnimatorController == null) return false;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        return true;
    }

    private void Enter(string state, float blend = .045f, float speed = 1f)
    {
        CurrentState = state;
        _elapsed = 0f;
        if (!EnsureAnimator()) return;
        animator.speed = speed;
        int hash = Animator.StringToHash("Base Layer." + state);
        if (blend <= 0f) animator.Play(hash, 0, 0f);
        else animator.CrossFadeInFixedTime(hash, blend, 0, 0f);
    }

    public void ResetIdle()
    {
        Enter("Idle", 0f);
        if (animator != null) animator.SetFloat(ChargeProgress, 0f);
    }

    public void SetCharge(float power)
    {
        if (CurrentState != "Charge") Enter("Charge", .035f);
        if (EnsureAnimator()) animator.SetFloat(ChargeProgress, Mathf.Clamp01(power));
    }

    public void PlayTakeoff(float flightDuration)
    {
        _takeoffDuration = Mathf.Clamp(flightDuration * .32f, .10f, .16f);
        Enter("Takeoff", .025f, .4f / _takeoffDuration);
    }

    public void PlayAirLoop() => Enter("AirLoop", .035f);
    public void PlayLand() => Enter("Land", .025f);
    public void PlayFall() => Enter("Fall", .045f);
    public void PlayCelebrate() => Enter("Celebrate", .06f);

    /// <summary>资源晚加载时直接采样当前动作进度，不重新播放已经过去的片段。</summary>
    public void Restore(string state, float elapsed, float power, float flightDuration)
    {
        elapsed = Mathf.Max(0f, elapsed);
        float length;
        switch (state)
        {
            case "Charge": SetCharge(power); length = .8f; elapsed = Mathf.Clamp01(power) * length; break;
            case "Takeoff":
                PlayTakeoff(flightDuration);
                length = _takeoffDuration;
                if (elapsed >= length) { elapsed -= length; PlayAirLoop(); length = 1f; }
                break;
            case "Land":
                if (elapsed >= 20f / 30f) { elapsed -= 20f / 30f; ResetIdle(); length = 2f; }
                else { PlayLand(); length = 20f / 30f; }
                break;
            case "Fall": PlayFall(); length = 1f; break;
            case "Celebrate":
                if (elapsed >= CelebrateDuration) { elapsed -= CelebrateDuration; ResetIdle(); }
                else PlayCelebrate();
                length = CelebrateDuration;
                break;
            default: ResetIdle(); length = 2f; break;
        }
        _elapsed = elapsed;
        if (!EnsureAnimator()) return;
        animator.Play(Animator.StringToHash("Base Layer." + CurrentState), 0, elapsed / length);
        animator.Update(0f);
    }

    private void Update() => Advance(Time.deltaTime);

    // A single state clock replaces delayed callbacks, so interruption is immediate.
    public void Advance(float deltaTime)
    {
        if (!isActiveAndEnabled || deltaTime <= 0f) return;
        _elapsed += deltaTime;
        switch (CurrentState)
        {
            case "Takeoff":
                if (_elapsed >= _takeoffDuration) PlayAirLoop();
                break;
            case "Land":
                if (_elapsed >= 20f / 30f) Enter("Idle", .04f);
                break;
            case "Celebrate":
                if (_elapsed >= CelebrateDuration) Enter("Idle", .06f);
                break;
        }
    }
}
