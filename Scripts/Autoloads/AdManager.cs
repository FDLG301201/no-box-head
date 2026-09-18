using Godot;
using System;
using PoingStudios.AdMob.Api;
using PoingStudios.AdMob.Api.Core;
using PoingStudios.AdMob.Api.Listeners;
using PoingStudios.AdMob.Ump.Api;
using PoingStudios.AdMob.Ump.Core;

namespace NoBoxHead;

/// <summary>
/// Single point of contact for AdMob. Every other script asks this class for an ad and gets a
/// plain callback back; nothing else in the game references the plugin, so swapping or removing
/// the ad SDK later touches one file.
///
/// Three rules are enforced here rather than at each call site, because a call site that forgets
/// one is not a bug you notice in testing — it is a policy violation you notice when the AdMob
/// account is suspended:
///
///  1. Off everywhere except mobile. On desktop and in the editor every method is a no-op that
///     reports "no ad available", so the rest of the game behaves as if ads did not exist.
///  2. Test ad units until <see cref="IsReal"/> is deliberately flipped. Clicking your own live
///     ads — even once, even while testing — is grounds for AdMob terminating the account.
///  3. Interstitials are rate-limited (see <see cref="InterstitialCooldown"/> and
///     <see cref="FreeDeaths"/>). Deaths in an arena shooter come every couple of minutes; an
///     interstitial on each one would be unplayable.
/// </summary>
public partial class AdManager : Node
{
    public static AdManager? Instance { get; private set; }

    /// <summary>
    /// FLIP THIS ONLY FOR A RELEASE BUILD, and never test with it on. While false the game uses
    /// Google's public test ad units, which serve real ad UI but earn nothing and are safe to
    /// tap. See the class comment.
    /// </summary>
    [Export] public bool IsReal { get; set; }

    // Google's official test units — safe to click, identical behaviour to live ads.
    private const string TestBanner       = "ca-app-pub-3940256099942544/6300978111";
    private const string TestInterstitial = "ca-app-pub-3940256099942544/1033173712";
    private const string TestRewarded     = "ca-app-pub-3940256099942544/5224354917";

    // The live ad units for "No Box Head" (Android), from the AdMob console. These are NOT
    // secrets — every ad unit id ships inside the APK and is readable by anyone who unpacks it,
    // so keeping them in source is normal.
    //
    // Set as code defaults rather than filled in from the inspector on purpose: this autoload is
    // registered as a bare script path, so Godot builds a plain Node for it and there is no
    // scene anywhere to store inspector values in. An [Export] with no scene behind it keeps
    // whatever the code says, which means the code is the only place these can actually live.
    //
    // They stay dormant until IsReal is switched on; see RealOrTest.
    [Export] public string RealBanner       { get; set; } = "ca-app-pub-5094321209577616/7065185885"; // menu-banner
    [Export] public string RealInterstitial { get; set; } = "ca-app-pub-5094321209577616/8115685311"; // regame-interstatial
    [Export] public string RealRewarded     { get; set; } = "ca-app-pub-5094321209577616/3054930322"; // respawn-bonus

    /// <summary>Minimum gap between interstitials, in seconds.</summary>
    private const double InterstitialCooldown = 90.0;

    /// <summary>
    /// Deaths at the start of a session that never show an interstitial. A player's first
    /// minutes decide whether they keep the game installed; that is the worst possible moment
    /// to interrupt them.
    /// </summary>
    private const int FreeDeaths = 2;

    private InterstitialAd? _interstitial;
    private RewardedAd?     _rewarded;
    private AdView?         _banner;

    private double _lastInterstitialAt = double.NegativeInfinity;
    private int    _deathsThisSession;
    private bool   _initialised;

    private static double Now => Time.GetTicksMsec() / 1000.0;

    /// <summary>True when a rewarded ad is loaded and can be shown right now.</summary>
    public bool IsRewardedReady => _rewarded != null;

    public override void _Ready()
    {
        Instance    = this;
        ProcessMode = ProcessModeEnum.Always; // the death screen pauses the tree

        // Desktop and editor never touch the SDK. The plugin only has native backends for
        // Android and iOS, so initialising it anywhere else logs errors and achieves nothing.
        if (!Platform.IsMobile) return;

        RequestConsentThenInitialise();
    }

    // ── Consent (UMP) ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs Google's User Messaging Platform flow and only then starts the ads SDK. The order
    /// matters and is not ours to choose: in the EEA and UK, requesting an ad before the consent
    /// state is known is the violation, so consent is resolved first and initialisation hangs off
    /// the end of it.
    ///
    /// Outside those regions UMP reports "not required" and the flow costs one callback.
    /// </summary>
    private void RequestConsentThenInitialise()
    {
        var consent = UserMessagingPlatform.ConsentInformation;

        consent.Update(
            new ConsentRequestParameters(),
            onSuccess: () =>
            {
                if (!consent.GetIsConsentFormAvailable()) { InitialiseSdk(); return; }

                UserMessagingPlatform.LoadConsentForm(
                    onSuccess: form => form.Show(_ => InitialiseSdk()),
                    // A form that will not load must not leave the game with ads permanently
                    // dead. Initialising anyway is what Google's own sample does: the SDK reads
                    // whatever consent state UMP did manage to store and limits itself
                    // accordingly, rather than serving personalised ads without permission.
                    onFailure: err => { GD.Print($"[AdManager] consent form failed: {err}"); InitialiseSdk(); });
            },
            onFailure: err => { GD.Print($"[AdManager] consent update failed: {err}"); InitialiseSdk(); });
    }

    /// <summary>
    /// True when this user must be offered a way to reopen the consent form — required in the
    /// EEA/UK once consent has been given. <see cref="ShowPrivacyOptions"/> is that way, and it
    /// needs an entry point in the settings menu; a build that returns true here and offers no
    /// button is not compliant.
    /// </summary>
    public bool PrivacyOptionsRequired =>
        Platform.IsMobile &&
        UserMessagingPlatform.ConsentInformation.GetPrivacyOptionsRequirementStatus()
            == ConsentInformation.PrivacyOptionsRequirementStatus.Required;

    /// <summary>Reopens the consent form so the user can change their mind.</summary>
    public void ShowPrivacyOptions()
    {
        if (!Platform.IsMobile) return;
        UserMessagingPlatform.ShowPrivacyOptionsForm(err =>
        {
            if (err != null) GD.Print($"[AdManager] privacy options failed: {err}");
        });
    }

    private void InitialiseSdk()
    {
        if (_initialised) return; // the flow above has several tails; only one may start the SDK

        MobileAds.Initialize(new OnInitializationCompleteListener
        {
            OnInitializationComplete = _ =>
            {
                _initialised = true;
                // Preload both full-screen formats immediately. An ad that starts loading only
                // when the player dies usually is not ready in time, and a "Watch ad to revive"
                // button that does nothing when tapped is worse than no button.
                LoadInterstitial();
                LoadRewarded();
            }
        });
    }

    private string RealOrTest(string real, string test, string label)
    {
        if (!IsReal) return test;
        if (!string.IsNullOrWhiteSpace(real)) return real;
        GD.PushWarning($"[AdManager] IsReal is on but no live ad unit id is set for {label}; " +
                       "falling back to the test unit. Set it in the AdManager autoload.");
        return test;
    }

    // ── Interstitial ──────────────────────────────────────────────────────────

    private void LoadInterstitial()
    {
        if (!Platform.IsMobile || !_initialised) return;
        new InterstitialAdLoader().Load(
            RealOrTest(RealInterstitial, TestInterstitial, "interstitial"),
            new AdRequest(),
            new InterstitialAdLoadCallback
            {
                OnAdLoaded       = ad  => _interstitial = ad,
                OnAdFailedToLoad = err => GD.Print($"[AdManager] interstitial failed: {err}"),
            });
    }

    /// <summary>
    /// Records a death and shows an interstitial if the caps allow it. Returns false when no ad
    /// was shown — the caller must carry on as normal in that case, never wait for a callback
    /// that will not arrive.
    ///
    /// Call this from the button that LEAVES the run (Menu), never from Retry. An ad appearing
    /// under a finger already travelling toward Retry produces accidental clicks, which AdMob
    /// treats as invalid traffic.
    /// </summary>
    public bool TryShowInterstitialOnDeath(Action? onClosed = null)
    {
        _deathsThisSession++;
        if (!Platform.IsMobile || _interstitial == null) return false;
        if (_deathsThisSession <= FreeDeaths) return false;
        if (Now - _lastInterstitialAt < InterstitialCooldown) return false;

        var ad = _interstitial;
        _interstitial = null; // an ad object is single-use; drop it before showing
        _lastInterstitialAt = Now;

        ad.FullScreenContentCallback = new FullScreenContentCallback
        {
            OnAdDismissedFullScreenContent = () => { onClosed?.Invoke(); ad.Destroy(); LoadInterstitial(); },
            OnAdFailedToShowFullScreenContent = _ => { onClosed?.Invoke(); ad.Destroy(); LoadInterstitial(); },
        };
        ad.Show();
        return true;
    }

    // ── Rewarded ──────────────────────────────────────────────────────────────

    private void LoadRewarded()
    {
        if (!Platform.IsMobile || !_initialised) return;
        new RewardedAdLoader().Load(
            RealOrTest(RealRewarded, TestRewarded, "rewarded"),
            new AdRequest(),
            new RewardedAdLoadCallback
            {
                OnAdLoaded       = ad  => _rewarded = ad,
                OnAdFailedToLoad = err => GD.Print($"[AdManager] rewarded failed: {err}"),
            });
    }

    /// <summary>
    /// Shows the opt-in rewarded ad. <paramref name="onEarned"/> fires only if the player
    /// actually watched far enough to earn the reward; <paramref name="onClosed"/> always fires
    /// afterwards, earned or not, so the caller can restore the UI in one place.
    ///
    /// Returns false if no ad is loaded, in which case neither callback ever runs. Check
    /// <see cref="IsRewardedReady"/> before offering the button at all.
    /// </summary>
    public bool ShowRewarded(Action onEarned, Action? onClosed = null)
    {
        if (!Platform.IsMobile || _rewarded == null) return false;

        var ad = _rewarded;
        _rewarded = null;
        bool earned = false;

        ad.FullScreenContentCallback = new FullScreenContentCallback
        {
            // Reward is granted on dismissal, not the instant it is earned, so the game does not
            // resume underneath an ad that is still on screen.
            OnAdDismissedFullScreenContent = () =>
            {
                if (earned) onEarned();
                onClosed?.Invoke();
                ad.Destroy();
                LoadRewarded();
            },
            OnAdFailedToShowFullScreenContent = _ =>
            {
                onClosed?.Invoke();
                ad.Destroy();
                LoadRewarded();
            },
        };
        ad.Show(new OnUserEarnedRewardListener { OnUserEarnedReward = _ => earned = true });
        return true;
    }

    // ── Banner ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Shows the menu banner. The banner is an OS-level overlay drawn on top of the game window,
    /// not a node in our scene, so the menu has to reserve space for it — see
    /// MainMenuUI. Overlapping it onto the buttons would cause accidental clicks.
    /// </summary>
    public void ShowMenuBanner(Action<float>? onHeightKnown = null)
    {
        if (!Platform.IsMobile || !_initialised) return;

        if (_banner != null)
        {
            _banner.Show();
            onHeightKnown?.Invoke(BannerHeightInUiUnits);
            return;
        }

        var view = new AdView(RealOrTest(RealBanner, TestBanner, "banner"),
                              AdSize.Banner, AdPosition.Bottom);
        // The height is not known until the ad actually loads, and the menu cannot reserve space
        // for a number it does not have yet. So the caller is told once, on load — never guessed
        // from a nominal dp figure, which would be wrong on every device with a different
        // density.
        view.AdListener = new AdListener
        {
            OnAdLoaded       = () => onHeightKnown?.Invoke(BannerHeightInUiUnits),
            OnAdFailedToLoad = err =>
            {
                GD.Print($"[AdManager] banner failed: {err}");
                onHeightKnown?.Invoke(0f); // no banner: the menu takes its space back
            },
        };
        _banner = view;
        view.LoadAd(new AdRequest());
    }

    /// <summary>Hides the banner when leaving the menu. Gameplay never shows one: it would sit
    /// over the arena and the touch controls.</summary>
    public void HideMenuBanner() => _banner?.Hide();

    /// <summary>Raw banner height in physical screen pixels, or 0 when none is showing.</summary>
    public int BannerHeightInPixels => _banner?.GetHeight() ?? 0;

    /// <summary>
    /// Banner height expressed in the units the UI is laid out in.
    ///
    /// These are not the same number. The banner is an OS overlay measured in real screen
    /// pixels, while the project stretches its canvas ("canvas_items" with "expand"), so a
    /// Control's 1 unit is some other number of physical pixels. Reserving the raw pixel count
    /// would leave a gap that is wrong by exactly the stretch factor — too small on a dense
    /// phone, which puts the bottom menu button underneath the banner, which is precisely the
    /// accidental-click situation AdMob penalises.
    /// </summary>
    public float BannerHeightInUiUnits
    {
        get
        {
            int px = BannerHeightInPixels;
            if (px <= 0) return 0f;
            var window = DisplayServer.WindowGetSize();
            if (window.Y <= 0) return px;
            float uiHeight = (float)GetViewport().GetVisibleRect().Size.Y;
            return px * (uiHeight / window.Y);
        }
    }

    public override void _ExitTree()
    {
        if (Instance == this) Instance = null;
    }
}
