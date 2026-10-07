using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Demo;
using StarBound.Economy;

namespace StarBound.UI
{
    // Classifies a round's outcome for EngagementScreen's impact feedback
    // (see PlayRoundOutcomeFeedback). Info is the only kind with no
    // animation (reserved for banners that genuinely have nothing to
    // show, not currently produced by anything) — every other kind now
    // drives a distinct portrait animation so a fight reads as an actual
    // battle: a shot always travels from attacker to defender (Hit/Crit/
    // Miss alike), initiative and escapes get their own beats too.
    public enum RoundBannerKind
    {
        Info,
        Initiative,
        Miss,
        Hit,
        CriticalHit,
        EscapeSuccess,
        EscapeFailed
    }

    // What the compact roll-math row (see EngagementScreen.CreateRollDetailRow)
    // shows for one roll — built by MatchHud's DescribeAttack/DescribeEscape/
    // DescribeInitiative, which already have the roll/total numbers needed
    // and derive the stat modifier arithmetically (modifier = total - roll)
    // rather than needing a separate Ship.GetStat lookup. Player is always
    // the left slot, Opponent always the right, regardless of which side
    // actually attacked/defended/escaped this round — the round banner
    // above already says who did what, so this row stays positionally
    // stable rather than flipping sides. default(RollDetailInfo) (null
    // PlayerFormula) means "nothing to show yet," same string.IsNullOrEmpty
    // convention used elsewhere in this file.
    public readonly struct RollDetailInfo
    {
        public IconGlyphMaterials.Glyph PlayerGlyph { get; }
        public string PlayerFormula { get; }
        public IconGlyphMaterials.Glyph OpponentGlyph { get; }
        public string OpponentFormula { get; }

        // Raw numbers behind PlayerFormula/OpponentFormula — kept
        // alongside the pre-formatted strings (used by the persistent
        // compact row) rather than replacing them, so the animated dice
        // reveal (see EngagementScreen.PlayRollRevealRoutine) can show
        // each beat — roll, then modifier, then total — without having
        // to re-parse a formatted string back apart.
        public int PlayerRoll { get; }
        public int PlayerStat { get; }
        public int PlayerTotal { get; }
        public int OpponentRoll { get; }
        public int OpponentStat { get; }
        public int OpponentTotal { get; }

        public RollDetailInfo(
            IconGlyphMaterials.Glyph playerGlyph, string playerFormula, int playerRoll, int playerStat, int playerTotal,
            IconGlyphMaterials.Glyph opponentGlyph, string opponentFormula, int opponentRoll, int opponentStat, int opponentTotal)
        {
            PlayerGlyph = playerGlyph;
            PlayerFormula = playerFormula;
            PlayerRoll = playerRoll;
            PlayerStat = playerStat;
            PlayerTotal = playerTotal;
            OpponentGlyph = opponentGlyph;
            OpponentFormula = opponentFormula;
            OpponentRoll = opponentRoll;
            OpponentStat = opponentStat;
            OpponentTotal = opponentTotal;
        }

        public bool HasValue => !string.IsNullOrEmpty(PlayerFormula);
    }


    // Full-screen, dedicated combat-resolution UI — replaces MatchHud's old
    // IMGUI DrawEngagementPanel/DrawConsumablesMidEngagement. "The most
    // frequent, highest-stakes interaction in the game" per this story,
    // so it gets the same glass/SDF treatment (via ScreenChromeKit) as the
    // other full-screen flow screens.
    //
    // Battle scene (added for the "[UI] Animated combat battle scene"
    // story): the two ships' real sprite art (see ShipMarkerView) face
    // off as the panel's visual centerpiece, with Hull/Energy bars (UGUI
    // native Image.Type.Filled) condensed below each — the standalone
    // Weapons/Shields/Speed stat row from the original stats-only panel
    // was dropped entirely to make room, per explicit user decision
    // ("scene takes over" over "shrink the scene to fit the stats").
    //
    // Created fresh per match by MatchHud (like MatchHudChrome), not once
    // in DemoBootstrap like TurnHandoffScreen/WinScreen — every value shown
    // is per-engagement state.
    //
    // Refresh is called explicitly at state-change points by MatchHud
    // (roll initiative, attack, brace/hold, escape, use item), same
    // discipline the dice bar already uses — NOT from a per-frame Update.
    // An earlier version rebuilt the action row via DestroyImmediate every
    // single visible frame, which silently broke every button: Unity's
    // Button needs the SAME GameObject to receive both pointer-down and
    // pointer-up, and recreating it mid-click meant no click ever
    // completed.
    public class EngagementScreen : MonoBehaviour
    {
        // Between PopupDialog's 150 and TurnHandoffScreen's 200 — never
        // shown concurrently with either, but keeps the sequence
        // monotonic with the app's other mutually-exclusive full-screen
        // states (same reasoning WinScreen's own SortingOrder comment
        // already uses).
        private const int SortingOrder = 180;
        private const float PanelWidth = 380f;
        // CreateGlassPanel's default horizontal padding (28f each side,
        // unchanged by the vertical-padding override below) subtracted
        // from PanelWidth — see CreatePanelText for why every long/
        // variable-length Text under the panel is built through that
        // wrapper rather than trusting VerticalLayoutGroup's
        // childForceExpandWidth on a bare Text alone.
        private const float PanelContentWidth = PanelWidth - 56f;
        private const float StatBarMinWidth = 60f;
        // Was 140f — at that size, the two portraits alone (280px) plus
        // every stat row/divider/banner stacked in the same
        // VerticalLayoutGroup pushed total panel content past the 866f
        // reference canvas height (ScreenChromeKit.ReferenceResolution),
        // clipping whatever landed above the panel's vertical center
        // (confirmed by rough content-height accounting: ~140px here,
        // 10f->8f spacing, and tighter panel padding below together claw
        // back enough margin that even a wrapped round-result line still
        // fits on screen).
        private const float PortraitSize = 110f;

        // Two-up action rows (Escape/Initiative, Brace/Hold) use explicit
        // half-widths rather than layout-group stretch, same reasoning as
        // MatchHudChrome's dice row; single-button states (Attack,
        // Continue) use the combined width so their footprint matches.
        // The opponent's three-up defense row (Escape/Brace/Hold, PvP
        // only) divides the same total width three ways instead.
        private const float ActionButtonWidth = 160f;
        private const float ActionRowSpacing = 8f;
        private const float FullActionWidth = ActionButtonWidth * 2f + ActionRowSpacing;
        private const float ThirdActionWidth = (FullActionWidth - ActionRowSpacing * 2f) / 3f;

        // Aggressive/combat action — same value as MatchHudChrome's
        // AttackColor, kept local since this screen has no other
        // dependency on that class.
        private static readonly Color CombatColor = new(0.7f, 0.25f, 0.25f);
        private static readonly Color HealthyBarColor = new(0.3f, 0.75f, 0.35f);
        private static readonly Color CriticalBarColor = new(0.75f, 0.25f, 0.25f);
        private static readonly Color DividerColor = new(1f, 1f, 1f, 0.12f);
        // Neither player's own color — a fiery, neutral "clash" accent
        // for the VS mark between the two combatant blocks.
        private static readonly Color VersusColor = new(0.95f, 0.55f, 0.2f);
        // Warm rather than pure white — pure white briefly flashed on a
        // stat bar read more like a UI glitch than an impact in an early
        // pass of this.
        private static readonly Color HitFlashColor = new(1f, 0.95f, 0.7f);

        // Crit gets a visibly bigger version of every effect below than a
        // regular hit — bigger pulse, bigger shake, longer/brighter bar
        // flash — so it actually reads as the bigger deal it is rather
        // than just a different color on the same-sized effect.
        private const float HitPulseScale = 1.15f;
        private const float CritPulseScale = 1.3f;
        private const float HitShakeMagnitude = 5f;
        private const float CritShakeMagnitude = 11f;
        private const float PanelShakeDuration = 0.25f;
        private const float HitBarFlashDuration = 0.25f;
        private const float CritBarFlashDuration = 0.4f;
        private const float WhiffShakeDuration = 0.3f;
        private const float WhiffShakeMagnitude = 8f;

        // Portrait combat juice — every attack (hit, crit, or miss alike)
        // fires a visible shot from attacker to defender first; a landed
        // one then lunges the attacker and shakes+flashes the defender, a
        // missed one dodges the defender instead (no flash/shake — same
        // "opposite of a landed blow" reasoning the existing whiff shake
        // already uses). The shot travels using RectTransform.position
        // (world/canvas space, read fresh each frame) rather than
        // anchoredPosition, which sidesteps the two portraits living in
        // independent LayoutGroup-driven rows — .position is only
        // unreliable to sample in the SAME frame a layout was just
        // rebuilt, and by the time a round actually resolves the panel's
        // been visible and settled for many frames already.
        private const float PortraitLungeDuration = 0.18f;
        private const float PortraitLungeScale = 1.18f;
        private const float PortraitImpactDuration = 0.3f;
        private const float PortraitImpactShakeMagnitude = 10f;
        // A soft radial glow (see GetOrCreateImpactFlashSprite) that pops
        // in and fades, plus a scatter of small sparks flung outward from
        // the hit point — replaces a plain flashing white rectangle
        // (Image with no sprite always renders as a flat-edged quad, same
        // underlying issue the stat bars had) with something that
        // actually reads as an impact. Crit gets a bigger pop and more,
        // faster, brighter sparks than a regular hit, same "visibly the
        // bigger deal" pattern every other crit effect already uses.
        private const int ImpactFlashTextureSize = 64;
        private const float ImpactFlashPopFraction = 0.3f;
        private const float ImpactFlashPeakScale = 1.05f;
        private const float ImpactFlashPeakScaleCrit = 1.3f;
        private const int ImpactSparkPoolSize = 7;
        private const int ImpactSparkCountHit = 4;
        private const int ImpactSparkCountCrit = 7;
        private const float ImpactSparkSize = 9f;
        private const float ImpactSparkTravelDistance = 42f;
        private const float ImpactSparkTravelDistanceCrit = 62f;
        private static readonly Color ImpactSparkColor = new(1f, 0.82f, 0.45f, 1f);
        private static readonly Color ImpactSparkColorCrit = new(1f, 0.95f, 0.75f, 1f);
        private const float PortraitDodgeDuration = 0.25f;
        private const float PortraitDodgeDistance = 14f;
        private const float ShotDuration = 0.22f;
        private const float ShotMissOffset = 70f;
        private static readonly Color ShotColor = new(1f, 0.92f, 0.55f, 1f);

        // The dice-roll reveal — plays BEFORE every other round-outcome
        // effect above (see PlayRoundOutcomeFeedback/RoundOutcomeSequenceRoutine)
        // so the numbers resolve first and the shot/impact/etc. read as
        // their consequence, not a simultaneous distraction. Built as a
        // real modal — a dimmed full-screen backdrop behind a properly
        // anchored, LayoutGroup-driven glass card (same visual language
        // CreateGlassPanel/GlassPanelMaterials already establish for the
        // rest of this screen) — NOT loose floating elements positioned
        // via manual world-space math. Two earlier attempts at the
        // latter produced two separate positioning bugs (a canvas-scale
        // unit mismatch, then a wrong reference point landing on the
        // opponent's stat bars) and still read as clutter floating over
        // the panel even once "correctly" positioned — explicit user
        // feedback: "it can't just float over what's underneath it."
        // Anchoring the card once at BuildUI time and never moving it
        // again removes that whole class of bug by construction; only
        // its CONTENT and its CanvasGroup-driven fade change per round.
        private const int RollBadgeTextureSize = 96;
        private const float RollBadgeSize = 60f;
        private const float RollCardWidth = 280f;
        private const float RollCardHeight = 140f;
        private const float RollBackdropMaxAlpha = 0.6f;
        private const float RollCardEntranceDuration = 0.3f;
        private const float RollRevealPopDuration = 0.35f;
        private const float RollRevealModifierDelay = 0.25f;
        private const float RollRevealModifierDuration = 0.3f;
        private const float RollRevealTotalDelay = 0.45f;
        private const float RollRevealTotalFlashDuration = 0.3f;
        private const float RollRevealHoldDuration = 0.6f;
        private const float RollRevealFadeDuration = 0.35f;
        private static readonly Color RollRevealTotalFlashColor = new(1f, 0.95f, 0.7f, 1f);
        // Winner emphasis, right after the Beat 3 flash settles — both
        // badges used to end up looking identical regardless of who
        // actually won the roll, leaving the reader to do the subtraction
        // themselves. The winning side grows slightly and gets a soft
        // halo (reuses GetOrCreateRollBadgeSprite at a larger padded
        // size, same sprite the badge itself already uses, tinted to
        // that side's own accent color); the loser is left exactly as
        // it already was — no shrink/dim, since "lost this roll" isn't
        // always a bad outcome (a Miss round's winning roll is the
        // DEFENDER's) and punishing the loser's visual would read wrong
        // there.
        private const float RollRevealWinnerEmphasisDuration = 0.25f;
        private const float RollRevealWinnerScale = 1.12f;
        private const float RollRevealWinnerGlowAlpha = 0.55f;
        private const float RollBadgeGlowPadding = 14f;
        // The stat icon beside each side's math text (e.g. "[Wpn icon]
        // +3 = 6") — so the number being added always carries which
        // stat it actually is, same icon the settled compact row
        // already uses for the same purpose. Sized up from the compact
        // row's own 16px (that row's icons sit next to small reference
        // text at a glance; this one is the visual focus of the whole
        // card and needs to actually read as an icon, not a stray mark).
        private const float RollMathIconSize = 22f;

        // Escape juice — success: the escapee slides off toward its own
        // side while fading out (it genuinely left — no "next round"
        // needs this portrait restored). Failure: the same dash, but only
        // a fraction of the way before snapping straight back with no
        // fade — "tried to bolt, got yanked back into the fight."
        private const float FleeDuration = 0.5f;
        private const float FleeFailPeakFraction = 0.45f;
        private const float FleeDistance = 160f;

        // The winner's pulse for taking initiative — still noticeably
        // quicker than a landed hit's pulse (reads as "readying to act,"
        // not "just got hit"), but bumped up from the original 0.18s/1.1x
        // (too subtle per feedback — easy to miss entirely) to a visibly
        // snappier pop, paired with an expanding accent-colored ring (see
        // InitiativeRing* below) so a round that was decided purely by a
        // Speed roll actually reads as a distinct, celebratory beat
        // instead of a near-invisible twitch.
        private const float ReadyPulseDuration = 0.3f;
        private const float ReadyPulseScale = 1.22f;

        // Expanding ring burst on whoever just won initiative — same
        // GetOrCreateShockwaveRingSprite silhouette the explosion's
        // shockwave uses, but tinted to the WINNER's own accent color
        // (plain Image tint works fine here, unlike IconGlyph icons —
        // this is an ordinary sprite, not a shared glyph material) and
        // sized to start hugging the portrait before racing outward past
        // it, instead of starting already oversized like the explosion's.
        private const float InitiativeRingDuration = 0.5f;
        private const float InitiativeRingSize = PortraitSize * 1.6f;
        private const float InitiativeRingStartScale = 0.55f;
        private const float InitiativeRingMaxScale = 1.3f;

        // The killing blow — a real radial burst (procedural starburst
        // texture, see GetOrCreateExplosionBurstSprite, not a tinted
        // rectangle) punches outward from the destroyed ship's own
        // position, flinging a scatter of small embers with it, while the
        // ship's own sprite fades and shrinks away and the whole panel
        // takes a stronger shake than even a crit's. Starts only once the
        // regular hit/crit impact has fully finished (see
        // PlayPortraitExplosion) so the two don't compete visually.
        private const float ExplosionDuration = 0.7f;
        private const float ExplosionShakeMagnitude = 16f;
        // Bigger than PortraitSize on purpose — a real blast overflows
        // the ship's own silhouette rather than staying confined to it.
        private const float ExplosionVisualSize = 220f;
        private const int ExplosionTextureSize = 128;
        // Points on the starburst silhouette — see GetOrCreateExplosionBurstSprite.
        private const int ExplosionRayCount = 10;
        private const int EmberCount = 7;
        private const float EmberSize = 16f;
        private const float EmberTravelDistance = 90f;
        // Random per-ember scale multiplier (applied on top of the
        // shrink-over-time curve already in PortraitExplosionRoutine) —
        // uniform-size embers read as a UI effect; varied ones read as
        // actual debris.
        private const float EmberSizeVarianceMin = 0.6f;
        private const float EmberSizeVarianceMax = 1.5f;
        // The ship breaking into 4 quadrant pieces — see
        // shipShardsRoot's own field comment for the masking technique.
        // Cell size is exactly half the portrait in each dimension so 4
        // of them tile it perfectly at rest.
        private const int ShipShardCount = 4;
        private const float ShipShardCellSize = PortraitSize / 2f;
        private const float ShipShardTravelDistance = 85f;
        private const float ShipShardMaxSpinSpeed = 260f;
        // Shards stay fully opaque (reconstructing the intact ship) for
        // the first chunk of the explosion, then fade as they separate —
        // matches how the main sprite's own fade is paced elsewhere in
        // this routine, so the "handoff" from intact ship to flying
        // debris doesn't look like two competing fades.
        private const float ShipShardFadeStartFraction = 0.45f;
        private static readonly Vector2[] ShipShardQuadrantOffsets =
        {
            new(-ShipShardCellSize / 2f, ShipShardCellSize / 2f),
            new(ShipShardCellSize / 2f, ShipShardCellSize / 2f),
            new(-ShipShardCellSize / 2f, -ShipShardCellSize / 2f),
            new(ShipShardCellSize / 2f, -ShipShardCellSize / 2f)
        };
        // A thin bright ring (see GetOrCreateShockwaveRingSprite) that
        // expands well past the main burst's own radius while fading —
        // "shockwave outrunning the fireball," the single biggest thing
        // missing from the plain burst+embers version per user feedback
        // ("still a bit simple").
        private const float ShockwaveMaxScale = 2.6f;
        private const float ShockwaveDurationFraction = 0.9f;
        // A couple of smaller, randomly-offset secondary bursts that pop
        // shortly after the main one — reads as a chain reaction rather
        // than one single clean blast.
        private const int SecondaryBurstCount = 2;
        private const float SecondaryBurstSize = 90f;
        private const float SecondaryBurstDuration = 0.3f;
        private const float SecondaryBurstMaxDelay = 0.18f;
        private const float SecondaryBurstOffsetRadius = 55f;

        // NPC opponents have no real Player/hull assignment to draw
        // from — stand in with one of the 4 player-hull sprites (Vanguard,
        // style 2, unused by either player) in a hostile tint, scaled up
        // by tier for a real (if placeholder) sense of escalating threat.
        // Real tiered NPC art (Assets/Resources/Ships/NPC/*.png) — baked-in
        // hostile color per the art-generation prompt, so these need no
        // runtime tint (unlike the Vanguard player-hull sprite below,
        // which is a last-resort fallback for if a name ever fails to
        // load and still needs NpcHostileTint to read as hostile at all).
        // Easy only has one design so far (the second Raider wasn't
        // delivered yet) — add its filename here once it exists, same
        // array shape as Medium/Hard.
        private static readonly string[] EasyNpcSpriteNames = { "raider_cutter" };
        private static readonly string[] MediumNpcSpriteNames = { "marauder_ravager", "marauder_reaver" };
        private static readonly string[] HardNpcSpriteNames = { "dread_behemoth", "dread_scourge" };
        private const int NpcPlaceholderHullStyle = 2;
        private static readonly Color NpcHostileTint = new(0.82f, 0.32f, 0.22f);

        private static string[] GetNpcSpriteNames(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => EasyNpcSpriteNames,
            EngagementTier.Medium => MediumNpcSpriteNames,
            EngagementTier.Hard => HardNpcSpriteNames,
            _ => MediumNpcSpriteNames
        };

        private static Sprite LoadNpcSprite(string name) => Resources.Load<Sprite>($"Ships/NPC/{name}");

        private static float GetNpcPortraitScale(EngagementTier tier) => tier switch
        {
            EngagementTier.Easy => 0.7f,
            EngagementTier.Medium => 1f,
            EngagementTier.Hard => 1.35f,
            _ => 1f
        };

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;
        private Text flavorText;

        // Floating above everything else (see BuildUI) so it can travel
        // freely between the two portraits regardless of where their own
        // LayoutGroup-driven rows place them.
        private RectTransform attackShotRect;
        private Image attackShotImage;
        private Coroutine attackShotCoroutine;
        private RectTransform explosionBurstRect;
        private Image explosionBurstImage;
        private RectTransform shockwaveRect;
        private Image shockwaveImage;
        private RectTransform[] secondaryBurstRects;
        private Image[] secondaryBurstImages;
        private RectTransform[] emberRects;
        private Image[] emberImages;
        // The destroyed ship itself breaking apart, alongside the burst/
        // shockwave/embers above — 4 quadrant pieces, each a RectMask2D
        // window onto the SAME ship sprite (see PlayPortraitExplosion's
        // own comment for the masking technique), rather than the whole
        // portrait just shrinking/fading as one piece. shipShardsRoot is
        // the one element ever given a WORLD .position (matching center,
        // same as explosionBurstRect etc.) — the 4 cells themselves are
        // positioned via LOCAL anchoredPosition relative to it, which is
        // what keeps their quadrant offsets and outward travel correctly
        // scaled without needing any canvas.scaleFactor math (the exact
        // bug class the dice-roll reveal hit twice before this).
        private RectTransform shipShardsRoot;
        private RectTransform[] shipShardCellRects;
        private Image[] shipShardInnerImages;
        // Shares the shockwave ring's silhouette but needs its own
        // RectTransform/Image — the shockwave one is already dedicated to
        // whichever portrait is exploding, and an initiative win can't
        // reuse that slot since nothing says the two effects could never
        // overlap for the two different portraits in a PvP match.
        private RectTransform initiativeRingRect;
        private Image initiativeRingImage;
        private Coroutine initiativeRingCoroutine;
        // A separate, smaller pool from the explosion's embers — impacts
        // (every landed hit/crit) are far more frequent than explosions
        // (once per engagement at most), and both can in principle be
        // mid-animation at once (impact fires, then the explosion
        // deliberately starts only after it finishes — see
        // PortraitExplosionRoutine's comment), so sharing a pool would
        // mean one stomps the other's in-flight sparks.
        private RectTransform[] impactSparkRects;
        private Image[] impactSparkImages;
        // Deliberately its own field rather than routed through
        // SetPortraitCoroutine's per-side slot — it needs to run AFTER
        // (not instead of) the impact coroutine that slot already holds
        // for this same round, not cancel it the instant it's queued.
        private Coroutine explosionCoroutine;

        // The dice-roll reveal modal (see PlayRollRevealRoutine) — a
        // dimmed backdrop behind a glass-styled card, same visual
        // language CreateGlassPanel/panelMaterial already establish for
        // this screen's own main panel (including needing its own
        // SyncPanelSize call every LateUpdate, see that method). The
        // card's own CanvasGroup drives the whole-card fade in/out; the
        // per-side badge/number/icon/math elements inside it only need
        // their own local scale/alpha for the beat-by-beat reveal, no
        // world-position math anywhere.
        private Image rollRevealBackdropImage;
        private RectTransform rollRevealCardRect;
        private Material rollRevealCardMaterial;
        private CanvasGroup rollRevealCardGroup;
        private Image playerRollBadge;
        private Image playerRollBadgeGlow;
        private Text playerRollNumberText;
        private Image playerRollMathIcon;
        private Text playerRollMathText;
        private Image opponentRollBadge;
        private Image opponentRollBadgeGlow;
        private Text opponentRollNumberText;
        private Image opponentRollMathIcon;
        private Text opponentRollMathText;
        // Owned by the outer RoundOutcomeSequenceRoutine (see
        // PlayRoundOutcomeFeedback), not this routine itself — stopping
        // the outer coroutine while it's suspended inside this one
        // (yield return StartCoroutine(...)) already halts this one too,
        // so there's no separate field needed here the way explosion/
        // attack-shot's own dedicated fields are needed for THEIR
        // independent stop-and-replace restarts.

        private Text opponentNameText;
        private Image opponentAccentUnderline;
        private RectTransform opponentPortraitRect;
        private Image opponentPortrait;
        private Image opponentPortraitFlash;
        private Coroutine opponentPortraitCoroutine;
        private Image opponentHullFill;
        private Text opponentHullValueText;
        private GameObject opponentEnergyRow;
        private Image opponentEnergyFill;
        private Text opponentEnergyValueText;
        // Weapons/Shields/Speed, always visible (PvE and PvP alike) —
        // before this, the only way to ever see either ship's stats was
        // to catch them flashing past in a roll-reveal, and only for
        // whichever stat that particular roll happened to use. Brace/
        // Hold/Escape decisions already ride on knowing these numbers.
        private Text opponentWeaponsValueText;
        private Text opponentShieldsValueText;
        private Text opponentSpeedValueText;

        private Text playerNameText;
        private Image playerAccentUnderline;
        private RectTransform playerPortraitRect;
        private Image playerPortrait;
        private Image playerPortraitFlash;
        private Coroutine playerPortraitCoroutine;
        private Image playerHullFill;
        private Text playerHullValueText;
        private Image playerEnergyFill;
        private Text playerEnergyValueText;
        private Text playerWeaponsValueText;
        private Text playerShieldsValueText;
        private Text playerSpeedValueText;

        private Text roundResultText;
        private Coroutine roundResultPulseCoroutine;
        private Coroutine defenderBarFlashCoroutine;
        private Coroutine panelShakeCoroutine;
        private Coroutine whiffShakeCoroutine;
        private Coroutine defenderReactionCoroutine;
        private string lastPulsedRoundResultText;
        // Narrower purpose than its name suggests now — the roll-math
        // breakdown moved to the icon row below (see rollDetailPlayerIcon
        // etc. and CreateRollDetailRow); this field/element is left in
        // place only for the "Used {item}." consumable-use message
        // (OnUseItemDuringEngagement), which is unrelated prose that
        // doesn't fit the icon row's shape.
        private Text rollDetailText;

        private Image rollDetailPlayerIcon;
        private Text rollDetailPlayerFormulaText;
        private Text rollDetailVsText;
        private Image rollDetailOpponentIcon;
        private Text rollDetailOpponentFormulaText;

        private Text actionInstructionText;
        private Transform actionRow;
        private Transform consumableRow;

        // True while a round's outcome is still being revealed (roll
        // reveal, shot, impact, explosion — see RevealRoundOutcome and
        // its call sites in RoundOutcomeSequenceRoutine/
        // DefenderReactionRoutine). RebuildActionRow renders a neutral
        // waiting state instead of the real one while this is true —
        // without it, e.g. a Brace/Hold prompt for the round that just
        // resolved would appear (and be tappable) before the player had
        // even seen who won the initiative roll that decided it.
        // Cleared, and the action row rebuilt with its own cached
        // params below, by RevealRoundOutcome itself once it's actually
        // time to show the new state — nothing else re-calls Refresh on
        // its own while this is pending, so the coroutine has to ask
        // for that rebuild directly rather than wait for one to happen.
        private bool actionRowRevealPending;
        private EngagementSession cachedActionRowSession;
        private string cachedActionRowOpponentDisplayName;
        private Color cachedActionRowOpponentAccentColor;
        private Color cachedActionRowPlayerAccentColor;
        private Action cachedOnAttemptEscape;
        private Action cachedOnRollInitiative;
        private Action cachedOnBrace;
        private Action cachedOnHold;
        private Action cachedOnAttack;
        private Action cachedOnContinue;
        private Action cachedOnOpponentAttemptEscape;
        private Action cachedOnOpponentBrace;
        private Action cachedOnOpponentHold;
        private Action cachedOnExecuteAttack;
        private Action cachedOnContinueEscapeIntercept;

        // Stat bars normalize against the value observed the first time a
        // given session is shown, not a fixed max — ships have no fixed
        // max stat in this game (repairs/items can raise them
        // indefinitely), so "starting value this fight" is the standard
        // stand-in for max-less stats. Reset whenever a new session
        // reference appears.
        private EngagementSession trackedSession;
        private int playerStartingHull;
        private int playerStartingEnergy;
        private int opponentStartingHull;
        private int opponentStartingEnergy;

        // What the Hull bars actually SHOW right now — deliberately not
        // always session's own live value. A Hit/CriticalHit round's
        // damage has already been applied to the ship stats by the time
        // Refresh sees it, so displaying it immediately would spoil the
        // round before the shot had even visibly landed (same reasoning
        // as roundResultText — see DefenderReactionRoutine, which is
        // what actually advances these to the real value once the shot
        // lands). Kept at the pre-round value in the meantime by simply
        // not being written to while hullRevealPending is true.
        private int displayedPlayerHull;
        private int displayedOpponentHull;
        private bool hullRevealPending;

        // Which NPC portrait this fight is using — picked once when a new
        // PvE session first appears (not re-rolled on every Refresh, same
        // "reset whenever a new session reference appears" discipline as
        // the starting-stat fields above) so the ship doesn't change
        // mid-fight. Color is white for real NPC art (already hostile-
        // tinted at generation time) or NpcHostileTint for the last-resort
        // Vanguard fallback, which still needs a runtime tint to read as
        // hostile at all.
        private Sprite npcPortraitSprite;
        private Color npcPortraitColor;

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate()
        {
            ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);
            ScreenChromeKit.SyncPanelSize(rollRevealCardMaterial, rollRevealCardRect);
        }

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void Hide() => SetVisible(false);

        public void Refresh(
            EngagementSession session, string opponentDisplayName, Color opponentAccentColor, Color playerAccentColor,
            Color activeAccentColor,
            string rollDetail, RollDetailInfo rollMathDetail, IReadOnlyList<ItemDefinition> usableConsumables,
            string roundResultMessage, Color roundResultColor, RoundBannerKind roundBannerKind, bool defenderIsPlayer,
            Sprite playerShipSprite, Sprite opponentShipSprite,
            Action onAttemptEscape, Action onRollInitiative, Action onBrace, Action onHold,
            Action onAttack, Action onContinue, Action<ItemDefinition> onUseItem,
            Action onOpponentAttemptEscape, Action onOpponentBrace, Action onOpponentHold,
            Action onExecuteAttack, Action onContinueEscapeIntercept)
        {
            if (session != trackedSession)
            {
                trackedSession = session;
                playerStartingHull = session.PlayerShip.GetStat(CoreStat.Hull);
                playerStartingEnergy = session.PlayerShip.GetStat(CoreStat.Energy);
                opponentStartingHull = new OpponentStatView(session.Opponent).Hull;
                // Opponent.GetStat is only meaningful for PvP (a real
                // player's ship) — for an NPC it's still a valid read
                // (NpcShipGenerator sets it), just never displayed below.
                opponentStartingEnergy = session.Opponent.GetStat(CoreStat.Energy);
                displayedPlayerHull = playerStartingHull;
                displayedOpponentHull = opponentStartingHull;
                hullRevealPending = false;

                if (!session.IsPvP)
                {
                    var names = GetNpcSpriteNames(session.Definition.Tier);
                    var chosenName = names[UnityEngine.Random.Range(0, names.Length)];
                    var realSprite = LoadNpcSprite(chosenName);
                    npcPortraitSprite = realSprite != null ? realSprite : ShipMarkerView.GetHullSprite(NpcPlaceholderHullStyle);
                    npcPortraitColor = realSprite != null ? Color.white : NpcHostileTint;
                }
            }

            // Neutral, third-party framing ("X vs Y") rather than "vs
            // {opponent}" from the current player's own point of view —
            // a PvP fight is a genuinely shared screen for its whole
            // duration (see the "[Multiplayer] PvP pass-and-play" story),
            // so a header written from either side's own perspective
            // would be just as wrong as "You"/"opponent" log text was.
            // This reads correctly no matter who's actually tapping next.
            headerText.text = session.IsPvP
                ? $"{session.Player.DisplayName} vs {opponentDisplayName}"
                : $"Engagement ({session.Definition.Tier})";
            headerText.color = activeAccentColor;
            panelMaterial.SetColor("_RimColor", activeAccentColor);
            accentBar.color = activeAccentColor;
            playerAccentUnderline.color = playerAccentColor;
            opponentAccentUnderline.color = opponentAccentColor;

            var showFlavor = !session.IsPvP && !string.IsNullOrEmpty(session.FlavorText);
            SetPanelTextVisible(flavorText, showFlavor);
            if (showFlavor)
                flavorText.text = session.FlavorText;

            // Player's own portrait is always a real assigned hull — PvE
            // and PvP alike. The opponent's is only a real hull in PvP
            // (opponentShipSprite non-null, passed by MatchHud from the
            // same PlayerOne/PlayerTwo hull-style source the map markers
            // use); for PvE it falls back to the hostile NPC placeholder,
            // scaled by this fight's tier.
            playerPortrait.sprite = playerShipSprite;
            playerPortrait.color = playerAccentColor;
            playerPortraitRect.localScale = Vector3.one;

            if (session.IsPvP)
            {
                opponentPortrait.sprite = opponentShipSprite;
                opponentPortrait.color = opponentAccentColor;
                opponentPortraitRect.localScale = Vector3.one;
            }
            else
            {
                opponentPortrait.sprite = npcPortraitSprite;
                opponentPortrait.color = npcPortraitColor;
                var npcScale = GetNpcPortraitScale(session.Definition.Tier);
                opponentPortraitRect.localScale = new Vector3(npcScale, npcScale, 1f);
            }

            // The opponent's own "accent" for UI chrome (roll icons, roll
            // badge/glow, initiative ring) — NOT the same thing as
            // npcPortraitColor, which only controls the ship SPRITE's
            // own multiply-tint and is deliberately Color.white for NPCs
            // with real pre-colored art (so their own actual colors show
            // through untinted). Reusing that white as an "accent color"
            // produced a plain white, unstyled-looking roll badge/icon
            // for exactly those NPCs — white isn't a color that reads as
            // "this is the opponent," it reads as "nothing was set."
            // NpcHostileTint is always a real, visible color regardless
            // of which way the portrait itself is tinted.
            var opponentShipColor = session.IsPvP ? opponentAccentColor : NpcHostileTint;

            // Must run BEFORE the Hull bars are displayed below, not
            // down in the round-result block where this conceptually
            // lives — detecting a brand-new Hit/CriticalHit message is
            // what holds the bar back, and doing that detection too
            // late meant the Hull bars below had already read and shown
            // session's live (already-damaged) value THIS SAME call,
            // using hullRevealPending's stale (still-false) state from
            // before this round started. Reused again down in the
            // round-result block itself (lastPulsedRoundResultText
            // isn't updated until there, so re-checking against it
            // later is still correct, just redundant computation).
            var isNewRoundResultMessage = !string.IsNullOrEmpty(roundResultMessage) && roundResultMessage != lastPulsedRoundResultText;
            if (isNewRoundResultMessage && roundBannerKind != RoundBannerKind.Info)
            {
                if (roundBannerKind is RoundBannerKind.Hit or RoundBannerKind.CriticalHit)
                    hullRevealPending = true;
                actionRowRevealPending = true;
            }

            var opponentView = new OpponentStatView(session.Opponent);
            opponentNameText.text = session.IsPvP ? opponentDisplayName : "Opponent";
            opponentNameText.color = opponentAccentColor;
            // See displayedOpponentHull's own comment — held at the
            // pre-round value while a Hit/CriticalHit's reveal is
            // pending, caught up to the real session value by
            // DefenderReactionRoutine once the shot actually lands.
            if (!hullRevealPending)
                displayedOpponentHull = opponentView.Hull;
            SetStatBar(opponentHullFill, displayedOpponentHull, opponentStartingHull);
            opponentHullValueText.text = displayedOpponentHull.ToString();

            // Energy only matters for a real player's ship — an NPC's
            // Energy is never consumed by anything (NPCs never brace or
            // escape), so showing it would just be noise. A PvP
            // opponent's Energy is a real, persistent resource of theirs
            // worth seeing.
            opponentEnergyRow.SetActive(session.IsPvP);
            if (session.IsPvP)
            {
                var opponentEnergy = session.Opponent.GetStat(CoreStat.Energy);
                SetStatBar(opponentEnergyFill, opponentEnergy, opponentStartingEnergy);
                opponentEnergyValueText.text = opponentEnergy.ToString();
            }

            // Weapons/Shields/Speed don't deplete over a fight the way
            // Hull/Energy do (no "current vs starting" concept, brace's
            // own +2 Shields is applied only within CombatResolver's own
            // math, never written back to the ship's stat) — a live read
            // every Refresh is correct, no reveal-deferral needed here.
            opponentWeaponsValueText.text = opponentView.Weapons.ToString();
            opponentShieldsValueText.text = opponentView.Shields.ToString();
            opponentSpeedValueText.text = opponentView.Speed.ToString();

            // "You" is only ever accurate from session.Player's own point
            // of view — a PvP fight is a shared screen both players act
            // on throughout, so there's no single "you" it could mean,
            // and PvP names it explicitly instead. Still "You" for NPC
            // fights, where there's no second real party to confuse it
            // with.
            playerNameText.text = session.IsPvP ? session.Player.DisplayName : "You";
            playerNameText.color = playerAccentColor;

            var ship = session.PlayerShip;
            if (!hullRevealPending)
                displayedPlayerHull = ship.GetStat(CoreStat.Hull);
            SetStatBar(playerHullFill, displayedPlayerHull, playerStartingHull);
            playerHullValueText.text = displayedPlayerHull.ToString();

            var playerEnergy = ship.GetStat(CoreStat.Energy);
            SetStatBar(playerEnergyFill, playerEnergy, playerStartingEnergy);
            playerEnergyValueText.text = playerEnergy.ToString();

            playerWeaponsValueText.text = ship.GetStat(CoreStat.Weapons).ToString();
            playerShieldsValueText.text = ship.GetStat(CoreStat.Shields).ToString();
            playerSpeedValueText.text = ship.GetStat(CoreStat.Speed).ToString();

            // Prominent outcome banner — cross-checking stat bars against
            // what just changed to figure out the outcome wasn't obvious
            // enough on its own.
            var hasRoundResult = !string.IsNullOrEmpty(roundResultMessage);
            SetPanelTextVisible(roundResultText, hasRoundResult);
            if (hasRoundResult)
            {
                // Only fire feedback when this is actually a NEW message —
                // Refresh can run again for unrelated reasons (e.g. a
                // consumable used afterward) without re-triggering it.
                // hullRevealPending/actionRowRevealPending are already
                // set above (see isNewRoundResultMessage's own comment —
                // they have to run before the Hull bars, earlier than
                // this block).
                if (roundBannerKind != RoundBannerKind.Info && isNewRoundResultMessage)
                {
                    lastPulsedRoundResultText = roundResultMessage;
                    // Text/color is deliberately NOT set here — for a
                    // Hit/CriticalHit/Miss it's set by
                    // DefenderReactionRoutine only once the shot has
                    // actually landed (or passed by, for a miss); for
                    // every other kind (no shot involved) it's set by
                    // RoundOutcomeSequenceRoutine once the dice-roll
                    // reveal itself has finished. Either way, setting it
                    // here, synchronously, would show "X takes damage!"
                    // (or similar) before the player had even seen the
                    // roll, let alone the shot.
                    PlayRoundOutcomeFeedback(roundBannerKind, defenderIsPlayer, session.Outcome, rollMathDetail, playerAccentColor, opponentShipColor, roundResultMessage, roundResultColor);
                }
                else
                {
                    // Either a repeat of an already-revealed message (e.g.
                    // Refresh re-running for an unrelated reason), or an
                    // Info banner, which never carries a roll to spoil —
                    // either way there's nothing to wait on.
                    roundResultText.text = roundResultMessage;
                    roundResultText.color = roundResultColor;
                }
            }

            // Item-use feedback only now ("Used {item}.") — the roll-math
            // breakdown this used to show moved to the icon row below.
            SetPanelTextVisible(rollDetailText, !string.IsNullOrEmpty(rollDetail));
            rollDetailText.text = rollDetail ?? string.Empty;

            // The compact roll-math row — icon + roll+stat=total per side.
            // Stays structurally present always (see CreateRollDetailRow's
            // own comment on why this fades via color alpha rather than
            // SetPanelTextVisible/SetActive); formula text going empty
            // already renders as nothing, but the icons need an explicit
            // fade since they're glyphs, not content-driven like text.
            // Tinted to each side's own accent color (per-instance
            // material, set once at CreateRollDetailSide — see its
            // comment) rather than IconGlyphMaterials.Get's shared one,
            // so these two icons read as "whose roll is this" at a
            // glance instead of both showing the same neutral blue.
            SetGlyphIcon(rollDetailPlayerIcon, rollMathDetail.PlayerGlyph, playerAccentColor);
            SetGlyphIcon(rollDetailOpponentIcon, rollMathDetail.OpponentGlyph, opponentShipColor);
            rollDetailPlayerFormulaText.text = rollMathDetail.PlayerFormula ?? string.Empty;
            rollDetailOpponentFormulaText.text = rollMathDetail.OpponentFormula ?? string.Empty;
            var rollMathAlpha = rollMathDetail.HasValue ? 1f : 0f;
            SetGraphicAlpha(rollDetailPlayerIcon, rollMathAlpha);
            SetGraphicAlpha(rollDetailOpponentIcon, rollMathAlpha);
            SetGraphicAlpha(rollDetailVsText, rollMathAlpha);

            // Cached unconditionally, every call — RevealRoundOutcome
            // needs these to rebuild the action row itself once a
            // pending reveal finishes, from inside a coroutine with no
            // other access to this call's own parameters.
            cachedActionRowSession = session;
            cachedActionRowOpponentDisplayName = opponentDisplayName;
            cachedActionRowOpponentAccentColor = opponentAccentColor;
            cachedActionRowPlayerAccentColor = playerAccentColor;
            cachedOnAttemptEscape = onAttemptEscape;
            cachedOnRollInitiative = onRollInitiative;
            cachedOnBrace = onBrace;
            cachedOnHold = onHold;
            cachedOnAttack = onAttack;
            cachedOnContinue = onContinue;
            cachedOnOpponentAttemptEscape = onOpponentAttemptEscape;
            cachedOnOpponentBrace = onOpponentBrace;
            cachedOnOpponentHold = onOpponentHold;
            cachedOnExecuteAttack = onExecuteAttack;
            cachedOnContinueEscapeIntercept = onContinueEscapeIntercept;

            RebuildActionRow(session, opponentDisplayName, opponentAccentColor, playerAccentColor,
                onAttemptEscape, onRollInitiative, onBrace, onHold, onAttack, onContinue,
                onOpponentAttemptEscape, onOpponentBrace, onOpponentHold,
                onExecuteAttack, onContinueEscapeIntercept);
            RebuildConsumableRow(usableConsumables, onUseItem);
        }

        private void RebuildActionRowFromCache() =>
            RebuildActionRow(cachedActionRowSession, cachedActionRowOpponentDisplayName, cachedActionRowOpponentAccentColor, cachedActionRowPlayerAccentColor,
                cachedOnAttemptEscape, cachedOnRollInitiative, cachedOnBrace, cachedOnHold, cachedOnAttack, cachedOnContinue,
                cachedOnOpponentAttemptEscape, cachedOnOpponentBrace, cachedOnOpponentHold,
                cachedOnExecuteAttack, cachedOnContinueEscapeIntercept);

        // Sets the round-result text/color AND clears actionRowRevealPending,
        // immediately rebuilding the action row with the cached params
        // above — the single moment, per kind, that a round's outcome
        // actually becomes visible (see this method's call sites in
        // RoundOutcomeSequenceRoutine/DefenderReactionRoutine for exactly
        // when that is per RoundBannerKind).
        private void RevealRoundOutcome(string message, Color color)
        {
            roundResultText.text = message;
            roundResultText.color = color;
            actionRowRevealPending = false;
            RebuildActionRowFromCache();
        }

        // Entry point for a round's feedback — plays the dice-roll reveal
        // duel first (see PlayRollRevealRoutine), then the kind-specific
        // effects below (PlayRoundOutcomeEffects) once the numbers have
        // actually resolved, so the shot/impact/etc. reads as the
        // consequence of a roll the player just watched happen, not a
        // simultaneous distraction. Same restart-cleanly discipline every
        // other Play* method in this file uses — a new round resolving
        // before a prior reveal+effects sequence finishes cancels it
        // cleanly (stopping this outer coroutine while it's suspended
        // inside the nested `yield return StartCoroutine(...)` below
        // already halts that nested routine too, no extra cleanup needed).
        private Coroutine roundOutcomeCoroutine;

        private void PlayRoundOutcomeFeedback(RoundBannerKind kind, bool subject, EngagementOutcome outcome, RollDetailInfo rollDetail, Color playerColor, Color opponentColor, string resultMessage, Color resultColor)
        {
            if (roundOutcomeCoroutine != null)
                StopCoroutine(roundOutcomeCoroutine);
            roundOutcomeCoroutine = StartCoroutine(RoundOutcomeSequenceRoutine(kind, subject, outcome, rollDetail, playerColor, opponentColor, resultMessage, resultColor));
        }

        private IEnumerator RoundOutcomeSequenceRoutine(RoundBannerKind kind, bool subject, EngagementOutcome outcome, RollDetailInfo rollDetail, Color playerColor, Color opponentColor, string resultMessage, Color resultColor)
        {
            if (rollDetail.HasValue)
            {
                // Which side actually won THIS roll — not always "subject"
                // as-is, since subject means something different per kind
                // (see PlayRoundOutcomeEffects' own per-case comments).
                // Hit/CriticalHit: subject is the DEFENDER, who just lost
                // the roll, so the winner is the other side. Miss and
                // EscapeSuccess: subject is the side that just succeeded.
                // EscapeFailed: subject is the side that just failed, so
                // again the winner is the other side. Initiative: subject
                // already IS the winner directly.
                var playerWonRoll = kind switch
                {
                    RoundBannerKind.Hit => !subject,
                    RoundBannerKind.CriticalHit => !subject,
                    RoundBannerKind.EscapeFailed => !subject,
                    _ => subject
                };
                yield return StartCoroutine(PlayRollRevealRoutine(rollDetail, playerColor, opponentColor, playerWonRoll));
            }

            // Hit/CriticalHit/Miss wait even longer than this — all the
            // way until the shot itself has landed or passed by, set
            // instead by DefenderReactionRoutine (see
            // PlayRoundOutcomeEffects' Hit/CriticalHit/Miss case). Every
            // other kind has no shot to wait for, so once the roll
            // reveal has actually finished (or was skipped entirely, for
            // a kind with no roll) is already the right moment.
            if (kind is not (RoundBannerKind.Hit or RoundBannerKind.CriticalHit or RoundBannerKind.Miss))
                RevealRoundOutcome(resultMessage, resultColor);

            PlayRoundOutcomeEffects(kind, subject, outcome, playerColor, opponentColor, resultMessage, resultColor);
            roundOutcomeCoroutine = null;
        }

        // Dispatches to a distinct animation per banner kind — a fight
        // should read as an actual battle moment to moment, not just a
        // changing number. `subject` means something different per kind
        // (documented at each case) rather than always "the defender",
        // since MatchHud's banner-building methods now report whichever
        // side is actually the featured actor for that particular event.
        // `outcome` is the session's outcome AS OF this same banner — a
        // Hit/CriticalHit that also happens to be the killing blow
        // (Outcome is PlayerWon/PlayerLost the instant Hull/Energy hits
        // zero, same round the damage lands) gets an explosion layered on
        // top of its ordinary impact effects.
        private void PlayRoundOutcomeEffects(RoundBannerKind kind, bool subject, EngagementOutcome outcome, Color playerColor, Color opponentColor, string resultMessage, Color resultColor)
        {
            switch (kind)
            {
                case RoundBannerKind.Hit:
                case RoundBannerKind.CriticalHit:
                case RoundBannerKind.Miss:
                {
                    // subject == defenderIsPlayer here.
                    var attackerPortrait = subject ? opponentPortraitRect : playerPortraitRect;
                    var defenderPortrait = subject ? playerPortraitRect : opponentPortraitRect;
                    var defenderFlash = subject ? playerPortraitFlash : opponentPortraitFlash;
                    var isMiss = kind == RoundBannerKind.Miss;
                    var isCrit = kind == RoundBannerKind.CriticalHit;
                    // Rolled once here, not inside PlayAttackShot, so the
                    // same side also drives PlayPortraitDodge (via
                    // PlayDefenderReaction below) — see PlayAttackShot's
                    // own comment for why these can't be independently
                    // randomized.
                    var missSide = UnityEngine.Random.value < 0.5f ? -1f : 1f;

                    // Every attack fires a visible shot, hit or not — a
                    // miss still needs something to dodge, not just a
                    // shake out of nowhere. The attacker's own lunge
                    // starts immediately (it's their own motion), but
                    // everything the DEFENDER does in response — dodge,
                    // shake, bar flash, impact, explosion — waits for the
                    // shot to actually travel there first (see
                    // PlayDefenderReaction); firing both at once used to
                    // mean the impact landed before the shot had visibly
                    // gone anywhere.
                    PlayAttackShot(attackerPortrait, defenderPortrait, isMiss, missSide);
                    PlayPortraitLunge(attackerPortrait, !subject);
                    PlayDefenderReaction(subject, outcome, defenderPortrait, defenderFlash, isMiss, isCrit, missSide, resultMessage, resultColor);
                    break;
                }

                case RoundBannerKind.EscapeSuccess:
                case RoundBannerKind.EscapeFailed:
                {
                    // subject == escapeeIsPlayer — the side attempting to
                    // flee, win or lose.
                    var escapeePortrait = subject ? playerPortraitRect : opponentPortraitRect;
                    var escapeeSprite = subject ? playerPortrait : opponentPortrait;
                    PlayPortraitFlee(escapeePortrait, escapeeSprite, !subject, kind == RoundBannerKind.EscapeSuccess);
                    break;
                }

                case RoundBannerKind.Initiative:
                {
                    // subject == winnerIsPlayer.
                    var winnerPortrait = subject ? playerPortraitRect : opponentPortraitRect;
                    var winnerColor = subject ? playerColor : opponentColor;
                    PlayPortraitReady(winnerPortrait, subject, winnerColor);
                    break;
                }
            }
        }

        // Restart-cleanly wrapper (same convention as every other
        // fire-and-forget effect coroutine in this file) around the
        // defender's half of a Hit/CriticalHit/Miss round — see
        // DefenderReactionRoutine. Needs its own tracked Coroutine
        // because it's started with a plain StartCoroutine call from
        // inside PlayRoundOutcomeEffects rather than yielded on, so
        // stopping the outer round-outcome sequence wouldn't otherwise
        // cancel a still-waiting (or already-landed) stale reaction
        // from a round that got interrupted.
        private void PlayDefenderReaction(bool subject, EngagementOutcome outcome, RectTransform defenderPortrait, Image defenderFlash, bool isMiss, bool isCrit, float missSide, string resultMessage, Color resultColor)
        {
            if (defenderReactionCoroutine != null)
                StopCoroutine(defenderReactionCoroutine);
            defenderReactionCoroutine = StartCoroutine(DefenderReactionRoutine(subject, outcome, defenderPortrait, defenderFlash, isMiss, isCrit, missSide, resultMessage, resultColor));
        }

        // Everything the DEFENDER does in reply to an attack — dodge,
        // shake, bar flash, impact, explosion-trigger — used to fire in
        // the same instant as PlayAttackShot's launch, so the impact
        // visibly landed before the shot had gone anywhere. Waiting out
        // ShotDuration here (the shot's own travel time) first makes the
        // reaction land when the shot actually arrives instead. The
        // plain-language result text and the Hull bar's own post-damage
        // value are ALSO held back until exactly this same moment — see
        // their own comments (RoundOutcomeSequenceRoutine, and
        // displayedPlayerHull/hullRevealPending) — so nothing about this
        // round is visible before the shot itself is.
        private IEnumerator DefenderReactionRoutine(bool subject, EngagementOutcome outcome, RectTransform defenderPortrait, Image defenderFlash, bool isMiss, bool isCrit, float missSide, string resultMessage, Color resultColor)
        {
            yield return new WaitForSeconds(ShotDuration);

            if (isMiss)
            {
                RevealRoundOutcome(resultMessage, resultColor);
                PlayWhiffShake();
                PlayPortraitDodge(defenderPortrait, subject, missSide);
                defenderReactionCoroutine = null;
                yield break;
            }

            // The defender losing this exact round is the only way
            // Outcome becomes a destruction result (escapes are handled
            // entirely separately, above) — check defenderIsPlayer ==
            // (outcome == PlayerLost) rather than just "is the fight
            // over," since PlayerWon/PlayerLost tells us WHICH side was
            // actually destroyed, not merely that it ended.
            var defenderWasDestroyed =
                (subject && outcome == EngagementOutcome.PlayerLost) ||
                (!subject && outcome == EngagementOutcome.PlayerWon);

            // An ordinary (non-fatal) hit reveals everything about this
            // round — message, Hull bar — right as the shot lands, same
            // as before. A killing blow holds BOTH back further still,
            // until the explosion itself (see below): showing Hull
            // already at 0, or "X takes N damage," during the ordinary
            // impact beat would land ahead of the explosion that's
            // actually revealing the destruction, not together with it.
            if (!defenderWasDestroyed)
            {
                RevealRoundOutcome(resultMessage, resultColor);
                RevealDefenderHull(subject);
            }

            PulseRoundResult(isCrit ? CritPulseScale : HitPulseScale);
            PlayPanelShake(isCrit ? CritShakeMagnitude : HitShakeMagnitude);
            PlayBarFlash(subject ? playerHullFill : opponentHullFill, isCrit ? CritBarFlashDuration : HitBarFlashDuration);
            PlayPortraitImpact(defenderPortrait, defenderFlash, subject, isCrit);

            if (defenderWasDestroyed)
            {
                PlayPortraitExplosion(defenderPortrait, subject ? playerPortrait : opponentPortrait);
                // PortraitExplosionRoutine itself waits this same
                // PortraitImpactDuration before its own burst actually
                // appears (started just above, in parallel with this
                // wait) — matching it here lands the Hull/message
                // reveal at the exact moment the explosion bursts, not
                // merely sometime before or after it.
                yield return new WaitForSeconds(PortraitImpactDuration);
                RevealRoundOutcome(resultMessage, resultColor);
                RevealDefenderHull(subject);
            }

            defenderReactionCoroutine = null;
        }

        // Shared by both reveal points in DefenderReactionRoutine above
        // (the ordinary immediate one, and the delayed-to-the-explosion
        // one for a killing blow). Stops any still-running bar flash
        // first — CritBarFlashDuration (0.4s) outlasts the explosion's
        // own PortraitImpactDuration wait (0.3s), so for a killing crit
        // the flash would otherwise still be mid-fade back to the OLD
        // resting color and immediately clobber the SetStatBar call
        // right below it.
        private void RevealDefenderHull(bool subject)
        {
            if (defenderBarFlashCoroutine != null)
            {
                StopCoroutine(defenderBarFlashCoroutine);
                defenderBarFlashCoroutine = null;
            }

            if (subject)
            {
                displayedPlayerHull = trackedSession.PlayerShip.GetStat(CoreStat.Hull);
                SetStatBar(playerHullFill, displayedPlayerHull, playerStartingHull);
                playerHullValueText.text = displayedPlayerHull.ToString();
            }
            else
            {
                displayedOpponentHull = new OpponentStatView(trackedSession.Opponent).Hull;
                SetStatBar(opponentHullFill, displayedOpponentHull, opponentStartingHull);
                opponentHullValueText.text = displayedOpponentHull.ToString();
            }
            hullRevealPending = false;
        }

        // The dice-roll reveal — three beats per side, in lockstep (see
        // the constants block near RollBadgeTextureSize for the full
        // reasoning). The card itself is anchored once at BuildUI time
        // and never repositioned — this routine only ever touches its
        // CONTENT (text/material) and its CanvasGroup/local-scale
        // animation, never a world .position. That's the actual fix for
        // the two earlier bugs here (a canvas-scale unit mismatch, then
        // a wrong reference point) — there's no position math left to
        // get wrong.
        private IEnumerator PlayRollRevealRoutine(RollDetailInfo detail, Color playerColor, Color opponentColor, bool playerWonRoll)
        {
            var playerBadgeRestColor = new Color(playerColor.r, playerColor.g, playerColor.b, 1f);
            var opponentBadgeRestColor = new Color(opponentColor.r, opponentColor.g, opponentColor.b, 1f);
            playerRollBadge.color = playerBadgeRestColor;
            opponentRollBadge.color = opponentBadgeRestColor;
            playerRollBadgeGlow.color = new Color(playerColor.r, playerColor.g, playerColor.b, 0f);
            opponentRollBadgeGlow.color = new Color(opponentColor.r, opponentColor.g, opponentColor.b, 0f);
            playerRollBadgeGlow.transform.localScale = Vector3.one;
            opponentRollBadgeGlow.transform.localScale = Vector3.one;
            playerRollNumberText.text = detail.PlayerRoll.ToString();
            opponentRollNumberText.text = detail.OpponentRoll.ToString();
            playerRollMathText.text = string.Empty;
            opponentRollMathText.text = string.Empty;
            // Tinted to each side's own accent color — see SetGlyphIcon.
            SetGlyphIcon(playerRollMathIcon, detail.PlayerGlyph, playerColor);
            SetGlyphIcon(opponentRollMathIcon, detail.OpponentGlyph, opponentColor);
            SetGraphicAlpha(playerRollNumberText, 0f);
            SetGraphicAlpha(opponentRollNumberText, 0f);
            SetGraphicAlpha(playerRollMathText, 0f);
            SetGraphicAlpha(opponentRollMathText, 0f);
            SetGraphicAlpha(playerRollMathIcon, 0f);
            SetGraphicAlpha(opponentRollMathIcon, 0f);
            playerRollBadge.transform.localScale = Vector3.one * 0.6f;
            opponentRollBadge.transform.localScale = Vector3.one * 0.6f;
            rollRevealCardRect.localScale = Vector3.one * 0.85f;
            rollRevealCardGroup.alpha = 0f;
            rollRevealBackdropImage.color = new Color(0f, 0f, 0f, 0f);
            // The backdrop's raycastTarget defaults to false (so it's
            // inert the rest of the time, between reveals) and is turned
            // on only for this routine's own lifetime — without it, the
            // dimmed backdrop LOOKED like a modal but every tap passed
            // straight through to whatever button or map hex sat behind
            // it. Reset here (every call, not just the first) rather
            // than only at the end, so an interrupted previous reveal
            // (a new round starting before the last one finished fading)
            // can't leave it stuck off before this one even begins.
            rollRevealBackdropImage.raycastTarget = true;

            // Entrance: backdrop dims the panel behind it, card pops in
            // as one unit — this is what actually separates the reveal
            // from the busy content underneath, instead of floating
            // directly over it.
            var elapsed = 0f;
            while (elapsed < RollCardEntranceDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RollCardEntranceDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                rollRevealBackdropImage.color = new Color(0f, 0f, 0f, RollBackdropMaxAlpha * t);
                rollRevealCardGroup.alpha = t;
                var cardScale = Mathf.Lerp(0.85f, 1f, eased);
                rollRevealCardRect.localScale = new Vector3(cardScale, cardScale, 1f);
                yield return null;
            }
            rollRevealBackdropImage.color = new Color(0f, 0f, 0f, RollBackdropMaxAlpha);
            rollRevealCardGroup.alpha = 1f;
            rollRevealCardRect.localScale = Vector3.one;

            // Beat 1: roll numbers pop in within their badges (ease-out-
            // cubic grow, same shape PortraitLungeRoutine/
            // PulseRoundResultRoutine already use elsewhere in this file).
            elapsed = 0f;
            while (elapsed < RollRevealPopDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RollRevealPopDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                var scale = Mathf.Lerp(0.6f, 1f, eased);
                playerRollBadge.transform.localScale = new Vector3(scale, scale, 1f);
                opponentRollBadge.transform.localScale = new Vector3(scale, scale, 1f);
                SetGraphicAlpha(playerRollNumberText, t);
                SetGraphicAlpha(opponentRollNumberText, t);
                yield return null;
            }
            playerRollBadge.transform.localScale = Vector3.one;
            opponentRollBadge.transform.localScale = Vector3.one;
            SetGraphicAlpha(playerRollNumberText, 1f);
            SetGraphicAlpha(opponentRollNumberText, 1f);

            yield return new WaitForSeconds(RollRevealModifierDelay);

            // Beat 2: the base stat fades in beside the roll — this is
            // the actual "differential" the user asked to see, shown as
            // its own distinct beat rather than folded straight into the
            // total.
            playerRollMathText.text = $"+{detail.PlayerStat}";
            opponentRollMathText.text = $"+{detail.OpponentStat}";
            elapsed = 0f;
            while (elapsed < RollRevealModifierDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RollRevealModifierDuration);
                SetGraphicAlpha(playerRollMathText, t);
                SetGraphicAlpha(opponentRollMathText, t);
                SetGraphicAlpha(playerRollMathIcon, t);
                SetGraphicAlpha(opponentRollMathIcon, t);
                yield return null;
            }
            SetGraphicAlpha(playerRollMathText, 1f);
            SetGraphicAlpha(opponentRollMathText, 1f);
            SetGraphicAlpha(playerRollMathIcon, 1f);
            SetGraphicAlpha(opponentRollMathIcon, 1f);

            yield return new WaitForSeconds(RollRevealTotalDelay);

            // Beat 3: merge into the total, with a brief warm flash on
            // each badge to draw the eye to the moment it resolves.
            playerRollMathText.text = $"+{detail.PlayerStat} = {detail.PlayerTotal}";
            opponentRollMathText.text = $"+{detail.OpponentStat} = {detail.OpponentTotal}";
            elapsed = 0f;
            while (elapsed < RollRevealTotalFlashDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RollRevealTotalFlashDuration);
                playerRollBadge.color = Color.Lerp(RollRevealTotalFlashColor, playerBadgeRestColor, t);
                opponentRollBadge.color = Color.Lerp(RollRevealTotalFlashColor, opponentBadgeRestColor, t);
                yield return null;
            }
            playerRollBadge.color = playerBadgeRestColor;
            opponentRollBadge.color = opponentBadgeRestColor;

            // Winner emphasis — grow the winning side's badge slightly
            // and bring up its halo, so which roll actually won reads
            // immediately rather than needing the reader to subtract the
            // two totals themselves (see RollRevealWinnerEmphasisDuration).
            var winnerBadge = playerWonRoll ? playerRollBadge : opponentRollBadge;
            var winnerGlow = playerWonRoll ? playerRollBadgeGlow : opponentRollBadgeGlow;
            var winnerGlowColor = playerWonRoll ? playerColor : opponentColor;
            elapsed = 0f;
            while (elapsed < RollRevealWinnerEmphasisDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / RollRevealWinnerEmphasisDuration);
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                var scale = Mathf.Lerp(1f, RollRevealWinnerScale, eased);
                winnerBadge.transform.localScale = new Vector3(scale, scale, 1f);
                winnerGlow.transform.localScale = new Vector3(scale, scale, 1f);
                winnerGlow.color = new Color(winnerGlowColor.r, winnerGlowColor.g, winnerGlowColor.b, RollRevealWinnerGlowAlpha * t);
                yield return null;
            }

            // Hold so the resolved numbers are actually readable, then
            // fade the whole modal away as one unit — the always-correct
            // compact row (RollDetailInfo, shown independently every
            // Refresh call) is what's left as the persistent record.
            yield return new WaitForSeconds(RollRevealHoldDuration);

            elapsed = 0f;
            while (elapsed < RollRevealFadeDuration)
            {
                elapsed += Time.deltaTime;
                var t = 1f - Mathf.Clamp01(elapsed / RollRevealFadeDuration);
                rollRevealBackdropImage.color = new Color(0f, 0f, 0f, RollBackdropMaxAlpha * t);
                rollRevealCardGroup.alpha = t;
                yield return null;
            }
            rollRevealBackdropImage.color = new Color(0f, 0f, 0f, 0f);
            rollRevealCardGroup.alpha = 0f;
            rollRevealBackdropImage.raycastTarget = false;
        }

        // Cheap, no-animation-package way to make a hit actually feel
        // like one — grow briefly, settle back. Restarts cleanly if
        // triggered again before finishing (shouldn't normally happen
        // given the "only on a new message" guard in Refresh, but a
        // stale coroutine reference would otherwise leak) — every other
        // Play* method below follows this same restart-cleanly shape.
        private void PulseRoundResult(float peakScale)
        {
            if (roundResultPulseCoroutine != null)
                StopCoroutine(roundResultPulseCoroutine);
            roundResultPulseCoroutine = StartCoroutine(PulseRoundResultRoutine(peakScale));
        }

        private IEnumerator PulseRoundResultRoutine(float peakScale)
        {
            var rect = roundResultText.rectTransform;
            var baseScale = Vector3.one;
            var peak = Vector3.one * peakScale;
            const float halfDuration = 0.16f;

            var elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                rect.localScale = Vector3.Lerp(baseScale, peak, elapsed / halfDuration);
                yield return null;
            }

            elapsed = 0f;
            while (elapsed < halfDuration)
            {
                elapsed += Time.deltaTime;
                rect.localScale = Vector3.Lerp(peak, baseScale, elapsed / halfDuration);
                yield return null;
            }

            rect.localScale = baseScale;
            roundResultPulseCoroutine = null;
        }

        // Jolts the whole panel (buttons included) briefly around its
        // known rest position — CreateGlassPanel never repositions this
        // panel away from (0,0), so that's used directly as the anchor to
        // restore to, rather than capturing "current" position, which
        // could already be mid-shake-offset if triggered again quickly.
        private void PlayPanelShake(float magnitude)
        {
            if (panelShakeCoroutine != null)
                StopCoroutine(panelShakeCoroutine);
            panelShakeCoroutine = StartCoroutine(PanelShakeRoutine(magnitude));
        }

        private IEnumerator PanelShakeRoutine(float magnitude)
        {
            var elapsed = 0f;
            while (elapsed < PanelShakeDuration)
            {
                elapsed += Time.deltaTime;
                var falloff = 1f - elapsed / PanelShakeDuration;
                var offset = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)) * magnitude * falloff;
                panelRect.anchoredPosition = offset;
                yield return null;
            }

            panelRect.anchoredPosition = Vector2.zero;
            panelShakeCoroutine = null;
        }

        // Flashes the defender's Hull bar to a bright "impact" color and
        // fades it back to whatever SetStatBar already computed for the
        // post-damage value (captured fresh each call, not a fixed
        // target) — SetStatBar always runs earlier in the same Refresh,
        // so fillImage.color is already the correct resting color when
        // this starts.
        private void PlayBarFlash(Image fillImage, float duration)
        {
            if (defenderBarFlashCoroutine != null)
                StopCoroutine(defenderBarFlashCoroutine);
            defenderBarFlashCoroutine = StartCoroutine(BarFlashRoutine(fillImage, duration));
        }

        private IEnumerator BarFlashRoutine(Image fillImage, float duration)
        {
            var restColor = fillImage.color;
            fillImage.color = HitFlashColor;

            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                fillImage.color = Color.Lerp(HitFlashColor, restColor, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            fillImage.color = restColor;
            defenderBarFlashCoroutine = null;
        }

        // A horizontal wobble on the round-result text only — no panel
        // shake, no bar flash, nothing landed. Reads as a dodge rather
        // than a quieter hit.
        private void PlayWhiffShake()
        {
            if (whiffShakeCoroutine != null)
                StopCoroutine(whiffShakeCoroutine);
            whiffShakeCoroutine = StartCoroutine(WhiffShakeRoutine());
        }

        private IEnumerator WhiffShakeRoutine()
        {
            var rect = roundResultText.rectTransform;

            // This coroutine starts synchronously inside the same Refresh()
            // call that just set roundResultText.text (and re-enabled it,
            // see SetPanelTextVisible) — before Unity's own deferred
            // layout rebuild pass has necessarily run for that change.
            // Forcing it here guarantees restPosition below is the real,
            // freshly-computed resting position, not whatever the rect
            // happened to still hold from before this round's content
            // change. Every other shake/dodge/flee routine in this file
            // (PortraitImpactRoutine, PortraitDodgeRoutine, PortraitFleeRoutine)
            // already captures-and-restores the actual anchoredPosition
            // instead of assuming a hardcoded value — this used to
            // hardcode a plain 0f (harmless back when roundResultText was
            // a direct, simple panel child; a real bug once CreatePanelText
            // nested it inside its own wrapper, where 0f is no longer
            // guaranteed to be the correct resting X).
            LayoutRebuilder.ForceRebuildLayoutImmediate(panelRect);
            var restPosition = rect.anchoredPosition;

            var elapsed = 0f;
            while (elapsed < WhiffShakeDuration)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / WhiffShakeDuration;
                var offset = Mathf.Sin(t * Mathf.PI * 5f) * WhiffShakeMagnitude * (1f - t);
                rect.anchoredPosition = restPosition + new Vector2(offset, 0f);
                yield return null;
            }

            rect.anchoredPosition = restPosition;
            whiffShakeCoroutine = null;
        }

        // Grows the attacker's portrait toward the shared center line and
        // back — "isOpponent" only decides which of the two shared
        // per-portrait coroutine fields to track, not the direction of
        // the effect (a uniform scale pulse has no direction to flip).
        private void PlayPortraitLunge(RectTransform portrait, bool isOpponent)
        {
            SetPortraitCoroutine(isOpponent, StartCoroutine(PortraitLungeRoutine(portrait)));
        }

        private IEnumerator PortraitLungeRoutine(RectTransform portrait)
        {
            var elapsed = 0f;
            while (elapsed < PortraitLungeDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Sin(Mathf.Clamp01(elapsed / PortraitLungeDuration) * Mathf.PI);
                var scale = Mathf.Lerp(1f, PortraitLungeScale, t);
                portrait.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            portrait.localScale = Vector3.one;
        }

        // Shake, same falloff shape as PlayPanelShake, plus a bright
        // flash overlay on the defender's portrait specifically (not the
        // whole panel) — the flash Image sits directly on top of the
        // portrait (see CreatePortraitBlock) and is otherwise fully
        // transparent.
        private void PlayPortraitImpact(RectTransform portrait, Image flash, bool isOpponent, bool isCrit)
        {
            SetPortraitCoroutine(isOpponent, StartCoroutine(PortraitImpactRoutine(portrait, flash, isCrit)));
        }

        private IEnumerator PortraitImpactRoutine(RectTransform portrait, Image flash, bool isCrit)
        {
            var magnitude = isCrit ? PortraitImpactShakeMagnitude * 1.4f : PortraitImpactShakeMagnitude;
            var restPosition = portrait.anchoredPosition;
            var center = portrait.position;

            var sparkCount = isCrit ? ImpactSparkCountCrit : ImpactSparkCountHit;
            var sparkTravel = isCrit ? ImpactSparkTravelDistanceCrit : ImpactSparkTravelDistance;
            var sparkColor = isCrit ? ImpactSparkColorCrit : ImpactSparkColor;
            var peakFlashScale = isCrit ? ImpactFlashPeakScaleCrit : ImpactFlashPeakScale;

            var sparkAngles = new float[ImpactSparkPoolSize];
            for (var i = 0; i < ImpactSparkPoolSize; i++)
            {
                sparkAngles[i] = UnityEngine.Random.Range(0f, 360f);
                impactSparkRects[i].position = center;
                var active = i < sparkCount;
                impactSparkImages[i].color = active ? sparkColor : new Color(sparkColor.r, sparkColor.g, sparkColor.b, 0f);
            }

            flash.rectTransform.localScale = Vector3.one * 0.6f;

            var elapsed = 0f;
            while (elapsed < PortraitImpactDuration)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / PortraitImpactDuration;
                var falloff = 1f - t;
                var offset = new Vector2(UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-1f, 1f)) * magnitude * falloff;
                portrait.anchoredPosition = restPosition + offset;

                // A quick pop up to peak scale over the first slice of
                // the duration, then it just fades at that size — reads
                // as a punch landing, not a static overlay sitting there.
                var popT = Mathf.Clamp01(t / ImpactFlashPopFraction);
                var flashScale = Mathf.Lerp(0.6f, peakFlashScale, 1f - Mathf.Pow(1f - popT, 2f));
                flash.rectTransform.localScale = new Vector3(flashScale, flashScale, 1f);
                flash.color = new Color(1f, 1f, 1f, falloff * (isCrit ? 0.9f : 0.65f));

                for (var i = 0; i < sparkCount; i++)
                {
                    var sparkT = Mathf.Clamp01(t * 1.3f); // finish slightly ahead of the flash/shake
                    var angleRad = sparkAngles[i] * Mathf.Deg2Rad;
                    var travel = sparkTravel * (1f - Mathf.Pow(1f - sparkT, 2f));
                    var sparkOffset = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f) * travel;
                    impactSparkRects[i].position = center + sparkOffset;
                    var sparkScale = Mathf.Lerp(1f, 0.15f, sparkT);
                    impactSparkRects[i].localScale = new Vector3(sparkScale, sparkScale, 1f);
                    impactSparkImages[i].color = new Color(sparkColor.r, sparkColor.g, sparkColor.b, 1f - sparkT);
                }

                yield return null;
            }

            portrait.anchoredPosition = restPosition;
            flash.color = new Color(1f, 1f, 1f, 0f);
            flash.rectTransform.localScale = Vector3.one;
            foreach (var sparkImage in impactSparkImages)
            {
                var c = sparkImage.color;
                sparkImage.color = new Color(c.r, c.g, c.b, 0f);
            }
        }

        // Quick sideways dodge-and-return — deliberately a translation,
        // not a shake, so it reads as "moved out of the way" rather than
        // "got hit but softer." Direction is derived from missSide (the
        // SAME world-space side AttackShotRoutine just sent the shot
        // past — see PlayAttackShot), not a fixed per-side convention —
        // the old hardcoded "opponent always -X, player always +X" rule
        // had nothing to do with the shot's own randomized miss side, so
        // they'd coincide about half the time and the defender would
        // visibly dodge INTO the shot's path instead of away from it.
        //
        // anchoredPosition is relative to the PARENT's (unrotated) axes,
        // not this rect's own — the opponent portrait's 180° rotation
        // (see CreatePortraitBlock's faceDown) only spins its own
        // content around its pivot for display, it does NOT change which
        // world-direction an anchoredPosition delta actually moves the
        // pivot itself. An InverseTransformDirection-based "correction"
        // here was tried and was wrong — it flipped the opponent's own
        // dodge relative to the shot on every single miss, instead of
        // leaving it matching the player's (already-correct) side. Plain
        // -missSide is correct for both sides uniformly.
        private void PlayPortraitDodge(RectTransform portrait, bool isOpponent, float missSide)
        {
            var direction = -missSide;
            SetPortraitCoroutine(isOpponent, StartCoroutine(PortraitDodgeRoutine(portrait, direction)));
        }

        private IEnumerator PortraitDodgeRoutine(RectTransform portrait, float direction)
        {
            var restPosition = portrait.anchoredPosition;
            var elapsed = 0f;
            while (elapsed < PortraitDodgeDuration)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / PortraitDodgeDuration;
                var offset = Mathf.Sin(t * Mathf.PI) * PortraitDodgeDistance * direction;
                portrait.anchoredPosition = restPosition + new Vector2(offset, 0f);
                yield return null;
            }

            portrait.anchoredPosition = restPosition;
        }

        private void SetPortraitCoroutine(bool isOpponent, Coroutine routine)
        {
            if (isOpponent)
            {
                if (opponentPortraitCoroutine != null)
                    StopCoroutine(opponentPortraitCoroutine);
                opponentPortraitCoroutine = routine;
            }
            else
            {
                if (playerPortraitCoroutine != null)
                    StopCoroutine(playerPortraitCoroutine);
                playerPortraitCoroutine = routine;
            }
        }

        // Travels attacker -> defender using world-space RectTransform.
        // position (see the field-block comment on why that's safe here).
        // A miss sails past to a random side instead of stopping dead-
        // center — a shot that visibly misses its mark, not one that just
        // vanishes on arrival. missSide is rolled by the CALLER (see the
        // Hit/CriticalHit/Miss case) rather than here, so the same value
        // can also drive PlayPortraitDodge — the defender needs to dodge
        // AWAY from wherever this shot actually ends up, not in some
        // independently-rolled direction that can coincidentally agree
        // with the shot's own miss side and read as "moved into it."
        private void PlayAttackShot(RectTransform attackerPortrait, RectTransform defenderPortrait, bool isMiss, float missSide)
        {
            if (attackShotCoroutine != null)
                StopCoroutine(attackShotCoroutine);
            attackShotCoroutine = StartCoroutine(AttackShotRoutine(attackerPortrait, defenderPortrait, isMiss, missSide));
        }

        private IEnumerator AttackShotRoutine(RectTransform attackerPortrait, RectTransform defenderPortrait, bool isMiss, float missSide)
        {
            var start = attackerPortrait.position;
            var end = defenderPortrait.position;
            if (isMiss)
                end += new Vector3(ShotMissOffset * missSide, 0f, 0f);

            attackShotImage.color = ShotColor;
            var elapsed = 0f;
            while (elapsed < ShotDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / ShotDuration);
                attackShotRect.position = Vector3.Lerp(start, end, t);
                var delta = end - start;
                if (delta.sqrMagnitude > 0.01f)
                    attackShotRect.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg - 90f);
                yield return null;
            }

            attackShotImage.color = new Color(1f, 1f, 1f, 0f);
            attackShotCoroutine = null;
        }

        // Success: the escapee slides toward its own side while fading
        // out — it's genuinely gone (the engagement is over), so it's
        // deliberately left faded/offset rather than restored. Failure:
        // the same dash, but only part way before snapping straight back
        // with no fade — "tried to bolt, got yanked back into the fight."
        private void PlayPortraitFlee(RectTransform portrait, Image sprite, bool isOpponentSide, bool success)
        {
            SetPortraitCoroutine(isOpponentSide, StartCoroutine(PortraitFleeRoutine(portrait, sprite, isOpponentSide ? -1f : 1f, success)));
        }

        private IEnumerator PortraitFleeRoutine(RectTransform portrait, Image sprite, float direction, bool success)
        {
            var restPosition = portrait.anchoredPosition;
            var restColor = sprite.color;
            var elapsed = 0f;

            if (success)
            {
                while (elapsed < FleeDuration)
                {
                    elapsed += Time.deltaTime;
                    var t = Mathf.Clamp01(elapsed / FleeDuration);
                    var eased = t * t;
                    portrait.anchoredPosition = restPosition + new Vector2(FleeDistance * direction * eased, 0f);
                    var color = sprite.color;
                    color.a = Mathf.Lerp(restColor.a, 0f, eased);
                    sprite.color = color;
                    yield return null;
                }

                yield break;
            }

            while (elapsed < FleeDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / FleeDuration);
                var travel = t < 0.5f
                    ? Mathf.Lerp(0f, FleeFailPeakFraction, t / 0.5f)
                    : Mathf.Lerp(FleeFailPeakFraction, 0f, (t - 0.5f) / 0.5f);
                portrait.anchoredPosition = restPosition + new Vector2(FleeDistance * direction * travel, 0f);
                yield return null;
            }

            portrait.anchoredPosition = restPosition;
            sprite.color = restColor;
        }

        private void PlayPortraitReady(RectTransform portrait, bool isPlayerSide, Color winnerColor)
        {
            SetPortraitCoroutine(!isPlayerSide, StartCoroutine(PortraitReadyRoutine(portrait)));

            if (initiativeRingCoroutine != null)
                StopCoroutine(initiativeRingCoroutine);
            initiativeRingCoroutine = StartCoroutine(InitiativeRingRoutine(portrait, winnerColor));
        }

        private IEnumerator PortraitReadyRoutine(RectTransform portrait)
        {
            var elapsed = 0f;
            while (elapsed < ReadyPulseDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Sin(Mathf.Clamp01(elapsed / ReadyPulseDuration) * Mathf.PI);
                var scale = Mathf.Lerp(1f, ReadyPulseScale, t);
                portrait.localScale = new Vector3(scale, scale, 1f);
                yield return null;
            }

            portrait.localScale = Vector3.one;
        }

        // The ring itself is a BattleEffectsLayer element independent of
        // the portrait's own transform (same reasoning as the shockwave
        // ring/attack shot) — it gets its one-time world .position from
        // the winning portrait here, then only ever animates its own
        // local scale/color, exactly the safe pattern established
        // earlier this session for ship-shards (one world-position
        // assignment, everything else local) to avoid the canvas-scale
        // unit-mismatch bug class the dice-roll reveal hit twice before.
        private IEnumerator InitiativeRingRoutine(RectTransform portrait, Color ringColor)
        {
            initiativeRingRect.position = portrait.position;

            var elapsed = 0f;
            while (elapsed < InitiativeRingDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / InitiativeRingDuration);
                // Ease-out expansion, paired with a fade that starts
                // immediately — a ring that's still near full-bright
                // once it's expanded past the portrait reads as hanging
                // around rather than racing outward and gone.
                var scale = Mathf.Lerp(InitiativeRingStartScale, InitiativeRingMaxScale, 1f - Mathf.Pow(1f - t, 2f));
                initiativeRingRect.localScale = new Vector3(scale, scale, 1f);
                initiativeRingImage.color = new Color(ringColor.r, ringColor.g, ringColor.b, (1f - t) * 0.85f);
                yield return null;
            }

            initiativeRingImage.color = new Color(ringColor.r, ringColor.g, ringColor.b, 0f);
            initiativeRingCoroutine = null;
        }

        private void PlayPortraitExplosion(RectTransform portrait, Image sprite)
        {
            if (explosionCoroutine != null)
                StopCoroutine(explosionCoroutine);
            explosionCoroutine = StartCoroutine(PortraitExplosionRoutine(portrait, sprite));
        }

        // Waits out the ordinary hit/crit impact entirely before firing —
        // "hit -> destroyed" as two clean beats, not two effects
        // competing for attention at once. The burst and every ember
        // start from the destroyed portrait's own world position (same
        // RectTransform.position technique the attack shot already uses)
        // so the explosion genuinely emanates from the ship, not from a
        // fixed point on screen.
        private IEnumerator PortraitExplosionRoutine(RectTransform portrait, Image sprite)
        {
            yield return new WaitForSeconds(PortraitImpactDuration);

            PlayPanelShake(ExplosionShakeMagnitude);

            var center = portrait.position;
            explosionBurstRect.position = center;
            explosionBurstRect.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));
            explosionBurstImage.color = Color.white;

            var emberAngles = new float[EmberCount];
            var emberSizeMultipliers = new float[EmberCount];
            for (var i = 0; i < EmberCount; i++)
            {
                // Evenly spread around the circle plus jitter, rather
                // than fully random, so embers don't occasionally clump
                // on one side and leave the other bare.
                emberAngles[i] = 360f / EmberCount * i + UnityEngine.Random.Range(-20f, 20f);
                emberSizeMultipliers[i] = UnityEngine.Random.Range(EmberSizeVarianceMin, EmberSizeVarianceMax);
                emberRects[i].position = center;
                emberImages[i].color = Color.white;
            }

            shockwaveRect.position = center;
            shockwaveImage.color = new Color(1f, 0.85f, 0.55f, 0f);

            // Randomly offset and delayed so the two don't look like
            // mirrored copies of each other or of the main burst.
            var secondaryOffsets = new Vector3[SecondaryBurstCount];
            var secondaryDelays = new float[SecondaryBurstCount];
            for (var i = 0; i < SecondaryBurstCount; i++)
            {
                var angle = UnityEngine.Random.Range(0f, 360f) * Mathf.Deg2Rad;
                var radius = UnityEngine.Random.Range(0.3f, 1f) * SecondaryBurstOffsetRadius;
                secondaryOffsets[i] = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * radius;
                secondaryDelays[i] = UnityEngine.Random.Range(0f, SecondaryBurstMaxDelay);
                secondaryBurstRects[i].position = center + secondaryOffsets[i];
                secondaryBurstImages[i].color = new Color(1f, 1f, 1f, 0f);
            }

            var spriteRestColor = sprite.color;

            // The ship breaking apart — see shipShardsRoot's own field
            // comment for the masking technique. shipShardsRoot is the
            // only piece given a world .position here; everything else
            // below is LOCAL anchoredPosition relative to it.
            shipShardsRoot.position = center;
            var shardSpinSpeeds = new float[ShipShardCount];
            for (var i = 0; i < ShipShardCount; i++)
            {
                shardSpinSpeeds[i] = UnityEngine.Random.Range(-ShipShardMaxSpinSpeed, ShipShardMaxSpinSpeed);
                shipShardCellRects[i].anchoredPosition = ShipShardQuadrantOffsets[i];
                shipShardCellRects[i].localRotation = Quaternion.identity;
                shipShardCellRects[i].localScale = Vector3.one;
                shipShardInnerImages[i].sprite = sprite.sprite;
                shipShardInnerImages[i].color = new Color(spriteRestColor.r, spriteRestColor.g, spriteRestColor.b, 1f);
            }

            var elapsed = 0f;

            while (elapsed < ExplosionDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / ExplosionDuration);

                // Fast outward punch over the first 30%, then hold/fade —
                // matches how a real blast front moves, not a steady
                // linear grow.
                var expandT = Mathf.Clamp01(t / 0.3f);
                var burstScale = Mathf.Lerp(0.15f, 1f, 1f - Mathf.Pow(1f - expandT, 3f));
                explosionBurstRect.localScale = new Vector3(burstScale, burstScale, 1f);
                var burstAlpha = t < 0.3f ? 1f : 1f - Mathf.Clamp01((t - 0.3f) / 0.7f);
                explosionBurstImage.color = new Color(1f, 1f, 1f, burstAlpha);

                // The ring: keeps expanding past the main burst's own
                // radius for the whole effect while a sine curve fades it
                // in then out (naturally zero at both ends, peak partway
                // through) — "outrunning the fireball."
                var shockT = Mathf.Clamp01(t / ShockwaveDurationFraction);
                var shockScale = Mathf.Lerp(0.3f, ShockwaveMaxScale, 1f - Mathf.Pow(1f - shockT, 2f));
                shockwaveRect.localScale = new Vector3(shockScale, shockScale, 1f);
                var shockAlpha = Mathf.Sin(shockT * Mathf.PI) * 0.9f;
                shockwaveImage.color = new Color(1f, 0.85f, 0.55f, shockAlpha);

                for (var i = 0; i < SecondaryBurstCount; i++)
                {
                    var localElapsed = elapsed - secondaryDelays[i];
                    if (localElapsed <= 0f)
                        continue;

                    var secondaryT = Mathf.Clamp01(localElapsed / SecondaryBurstDuration);
                    var secondaryScale = Mathf.Lerp(0.2f, 1f, secondaryT);
                    secondaryBurstRects[i].localScale = new Vector3(secondaryScale, secondaryScale, 1f);
                    var secondaryAlpha = Mathf.Sin(secondaryT * Mathf.PI);
                    secondaryBurstImages[i].color = new Color(1f, 1f, 1f, secondaryAlpha);
                }

                for (var i = 0; i < EmberCount; i++)
                {
                    var emberT = Mathf.Clamp01(t * 1.2f); // finish slightly ahead of the main burst
                    var angleRad = emberAngles[i] * Mathf.Deg2Rad;
                    var travel = EmberTravelDistance * (1f - Mathf.Pow(1f - emberT, 2f));
                    var offset = new Vector3(Mathf.Cos(angleRad), Mathf.Sin(angleRad), 0f) * travel;
                    emberRects[i].position = center + offset;
                    var emberScale = Mathf.Lerp(1f, 0.2f, emberT) * emberSizeMultipliers[i];
                    emberRects[i].localScale = new Vector3(emberScale, emberScale, 1f);
                    emberImages[i].color = new Color(1f, 1f, 1f, 1f - emberT);
                }

                // The ship's own 4 pieces flying apart — each travels
                // outward in its own quadrant's natural direction (so a
                // piece that started top-left keeps heading further
                // top-left, reading as physically plausible rather than
                // random), spinning at its own randomized rate, fading
                // out only once it's visibly separated (see
                // ShipShardFadeStartFraction).
                for (var i = 0; i < ShipShardCount; i++)
                {
                    var direction = ShipShardQuadrantOffsets[i].normalized;
                    var travel = ShipShardTravelDistance * (1f - Mathf.Pow(1f - t, 2f));
                    shipShardCellRects[i].anchoredPosition = ShipShardQuadrantOffsets[i] + direction * travel;
                    shipShardCellRects[i].localRotation = Quaternion.Euler(0f, 0f, shardSpinSpeeds[i] * elapsed);
                    var shardScale = Mathf.Lerp(1f, 0.55f, t);
                    shipShardCellRects[i].localScale = new Vector3(shardScale, shardScale, 1f);
                    var shardAlpha = t < ShipShardFadeStartFraction ? 1f : 1f - Mathf.Clamp01((t - ShipShardFadeStartFraction) / (1f - ShipShardFadeStartFraction));
                    var shardColor = shipShardInnerImages[i].color;
                    shardColor.a = shardAlpha;
                    shipShardInnerImages[i].color = shardColor;
                }

                var spriteColor = spriteRestColor;
                spriteColor.a = Mathf.Lerp(spriteRestColor.a, 0f, t);
                sprite.color = spriteColor;
                var portraitScale = Mathf.Lerp(1f, 0.3f, t);
                portrait.localScale = new Vector3(portraitScale, portraitScale, 1f);

                yield return null;
            }

            // Burst/ring/secondary-bursts/embers/shards faded back to
            // invisible; the ship sprite itself deliberately left faded/
            // shrunk, not restored — the engagement is over (this is the
            // killing blow), same "no next round needs this portrait
            // back" reasoning PlayPortraitFlee's success case uses.
            explosionBurstImage.color = new Color(1f, 1f, 1f, 0f);
            shockwaveImage.color = new Color(1f, 0.85f, 0.55f, 0f);
            foreach (var emberImage in emberImages)
                emberImage.color = new Color(1f, 1f, 1f, 0f);
            foreach (var secondaryImage in secondaryBurstImages)
                secondaryImage.color = new Color(1f, 1f, 1f, 0f);
            foreach (var shardImage in shipShardInnerImages)
            {
                var c = shardImage.color;
                shardImage.color = new Color(c.r, c.g, c.b, 0f);
            }
            explosionCoroutine = null;
        }

        private static void SetStatBar(Image fillImage, int currentValue, int startingValue)
        {
            var fraction = startingValue > 0 ? Mathf.Clamp01((float)currentValue / startingValue) : 0f;
            fillImage.fillAmount = fraction;
            fillImage.color = Color.Lerp(CriticalBarColor, HealthyBarColor, fraction);
        }

        // Every branch below explicitly names whoever is currently
        // expected to act, and tints actionInstructionText to that
        // side's own accent color — confirmed confusing without this:
        // "who's actually the active player right now" wasn't clear
        // enough from the buttons/state alone.
        private void RebuildActionRow(
            EngagementSession session, string opponentDisplayName, Color opponentAccentColor, Color playerAccentColor,
            Action onAttemptEscape, Action onRollInitiative, Action onBrace, Action onHold, Action onAttack, Action onContinue,
            Action onOpponentAttemptEscape, Action onOpponentBrace, Action onOpponentHold,
            Action onExecuteAttack, Action onContinueEscapeIntercept)
        {
            for (var i = actionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(actionRow.GetChild(i).gameObject);

            // A round's outcome (who won initiative, whether the attack
            // landed, even whether the engagement just ENDED) is already
            // fully resolved in session by the time this runs — same as
            // roundResultText/the Hull bars, showing the next prompt (or
            // "Continue") here immediately would let the player act on
            // (or skip past) a round they haven't actually seen resolve
            // yet. No buttons, no instruction text, until
            // RevealRoundOutcome says it's time (see its own comment).
            if (actionRowRevealPending)
            {
                actionInstructionText.gameObject.SetActive(false);
                return;
            }

            // Defaults to visible every rebuild — only the empty-string
            // escape-outcome case below turns it back off.
            actionInstructionText.gameObject.SetActive(true);

            var playerName = session.Player.DisplayName;

            if (session.Outcome != EngagementOutcome.InProgress)
            {
                var outcomeText = DescribeOutcome(session.Outcome, playerName, opponentDisplayName, session.IsPvP);
                // Escape outcomes describe as empty (see DescribeOutcome) —
                // the round banner already said it, so hide the line
                // entirely rather than leaving a blank gap where it was.
                actionInstructionText.gameObject.SetActive(!string.IsNullOrEmpty(outcomeText));
                actionInstructionText.text = outcomeText;
                actionInstructionText.color = Color.white;
                var (continueButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Continue", ScreenChromeKit.ConfirmColor,
                    true, GlassPanelMaterials.Style.DieButton, width: FullActionWidth, height: 52f, fontSize: 18);
                continueButton.onClick.AddListener(() => onContinue?.Invoke());
                return;
            }

            // PvP shared-screen combat: both sides act on this one screen
            // for the whole fight now (see Story "[Multiplayer] PvP pass-
            // and-play: shared-screen combat"), no more full-screen hand-
            // off in between — so every pending-decision state below
            // needs its OWN branch here, rather than some of them being
            // handled entirely by a hand-off screen's own button like
            // before. Checked before every other PvP state below since
            // EngagementSession.BeginEscapeAttempt/BeginOpponentEscapeAttempt
            // can both leave IsAwaitingAttackResolution false in the exact
            // same way the plain pre-round check further down relies on.
            if (session.IsPvP && session.IsAwaitingEscapeIntercept)
            {
                var escapeeIsPlayer = session.PendingEscapee == RoundAttacker.Player;
                var escapeeName = escapeeIsPlayer ? playerName : opponentDisplayName;
                var interceptorName = escapeeIsPlayer ? opponentDisplayName : playerName;
                var interceptorColor = escapeeIsPlayer ? opponentAccentColor : playerAccentColor;

                actionInstructionText.text = $"{escapeeName} is trying to escape — {interceptorName}, stop them!";
                actionInstructionText.color = interceptorColor;

                var (interceptButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Try to Stop Them!", CombatColor,
                    true, GlassPanelMaterials.Style.DieButton, width: FullActionWidth, height: 52f, fontSize: 16);
                interceptButton.onClick.AddListener(() => onContinueEscapeIntercept?.Invoke());
                return;
            }

            if (!session.IsAwaitingAttackResolution)
            {
                // Pre-initiative is always the current player's own
                // moment now, PvP included — Initiative resolves in one
                // call (see EngagementSession.ResolveInitiative's own
                // comment), so there's no separate opponent-side
                // "roll your half" sub-phase to branch on here anymore.
                actionInstructionText.text = $"{playerName}: choose your move.";
                actionInstructionText.color = playerAccentColor;

                var (escapeButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Attempt Escape",
                    session.CanAttemptEscape ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                    session.CanAttemptEscape, GlassPanelMaterials.Style.DieButton, width: ActionButtonWidth, height: 56f, fontSize: 14);
                escapeButton.onClick.AddListener(() => onAttemptEscape?.Invoke());

                var (initiativeButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Roll Initiative", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.DieButton, width: ActionButtonWidth, height: 56f, fontSize: 14);
                initiativeButton.onClick.AddListener(() => onRollInitiative?.Invoke());
                return;
            }

            // Whichever side actually won initiative gets their own
            // "Attack!" tap once the defender has declared Brace/Hold
            // (PvP only; see EngagementSession.DeclareDefense/ExecuteAttack)
            // — checked BEFORE the Brace/Hold branch below, since
            // PendingAttacker doesn't change until ExecuteAttack actually
            // runs, so "PendingAttacker == Opponent" alone can't tell
            // apart "player still needs to declare" from "opponent
            // already declared, now the player needs to attack."
            if (session.IsPvP && session.IsAwaitingAttackExecution)
            {
                var attackerIsPlayer = session.PendingAttacker == RoundAttacker.Player;
                var attackerName = attackerIsPlayer ? playerName : opponentDisplayName;
                var attackerColor = attackerIsPlayer ? playerAccentColor : opponentAccentColor;

                actionInstructionText.text = $"{attackerName}, you have the initiative — attack!";
                actionInstructionText.color = attackerColor;

                var (executeAttackButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Attack", CombatColor,
                    true, GlassPanelMaterials.Style.DieButton, width: FullActionWidth, height: 52f, fontSize: 18);
                executeAttackButton.onClick.AddListener(() => onExecuteAttack?.Invoke());
                return;
            }

            if (session.PendingAttacker == RoundAttacker.Opponent)
            {
                actionInstructionText.text = $"{playerName}, brace for impact!";
                actionInstructionText.color = playerAccentColor;

                var canBrace = session.PlayerShip.GetStat(CoreStat.Energy) > 0;
                var (braceButton, _, _) = ScreenChromeKit.CreateButton(actionRow,
                    $"Brace (-{CombatResolver.BraceEnergyCost} Nrg, +{CombatResolver.BraceShieldBonus} Shld)",
                    canBrace ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                    canBrace, GlassPanelMaterials.Style.DieButton, width: ActionButtonWidth, height: 56f, fontSize: 12);
                braceButton.onClick.AddListener(() => onBrace?.Invoke());

                var (holdButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Hold", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.DieButton, width: ActionButtonWidth, height: 56f, fontSize: 14);
                holdButton.onClick.AddListener(() => onHold?.Invoke());
                return;
            }

            // PendingAttacker == Player from here on — for an NPC fight
            // that's always just "you attack" (below). For PvP, the
            // opponent (the actual defender) gets their own real choice
            // first instead of the current player unilaterally resolving
            // it — shown directly on this shared screen now, same as
            // every other PvP decision point above.
            if (session.CanOpponentDecideDefense)
            {
                actionInstructionText.text = $"{opponentDisplayName}: escape, brace, or hold?";
                actionInstructionText.color = opponentAccentColor;

                var (opponentEscapeButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Escape",
                    session.Definition.EscapeAllowed ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                    session.Definition.EscapeAllowed, GlassPanelMaterials.Style.DieButton, width: ThirdActionWidth, height: 56f, fontSize: 12);
                opponentEscapeButton.onClick.AddListener(() => onOpponentAttemptEscape?.Invoke());

                var opponentCanBrace = session.Opponent.GetStat(CoreStat.Energy) > 0;
                var (opponentBraceButton, _, _) = ScreenChromeKit.CreateButton(actionRow,
                    $"Brace (-{CombatResolver.BraceEnergyCost} Nrg, +{CombatResolver.BraceShieldBonus} Shld)",
                    opponentCanBrace ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                    opponentCanBrace, GlassPanelMaterials.Style.DieButton, width: ThirdActionWidth, height: 56f, fontSize: 12);
                opponentBraceButton.onClick.AddListener(() => onOpponentBrace?.Invoke());

                var (opponentHoldButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Hold", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.DieButton, width: ThirdActionWidth, height: 56f, fontSize: 12);
                opponentHoldButton.onClick.AddListener(() => onOpponentHold?.Invoke());
                return;
            }

            // NPC fights only reach here — Player is attacking and the
            // NPC never gets a declare step, so this is the single
            // combined resolve (onAttack), the only Attack button this
            // screen ever shows.
            actionInstructionText.text = $"{playerName}, you have the initiative — attack!";
            actionInstructionText.color = playerAccentColor;
            var (attackButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Attack", CombatColor,
                true, GlassPanelMaterials.Style.DieButton, width: FullActionWidth, height: 52f, fontSize: 18);
            attackButton.onClick.AddListener(() => onAttack?.Invoke());
        }

        // Escape outcomes are already announced by the round banner above
        // ("{name} escaped!") — restating them here as "Outcome:
        // PlayerEscaped" was pure noise. Win/Loss get a proper sentence
        // instead, since nothing else on screen says who actually won.
        private static string DescribeOutcome(EngagementOutcome outcome, string playerName, string opponentDisplayName, bool isPvP) => outcome switch
        {
            EngagementOutcome.PlayerWon => isPvP ? $"{playerName} wins!" : "Victory!",
            EngagementOutcome.PlayerLost => isPvP ? $"{opponentDisplayName} wins!" : "Defeat.",
            EngagementOutcome.PlayerEscaped or EngagementOutcome.OpponentEscaped => string.Empty,
            _ => outcome.ToString()
        };

        private void RebuildConsumableRow(IReadOnlyList<ItemDefinition> usableConsumables, Action<ItemDefinition> onUseItem)
        {
            for (var i = consumableRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(consumableRow.GetChild(i).gameObject);

            foreach (var item in usableConsumables)
            {
                var (button, _, _) = ScreenChromeKit.CreateButton(consumableRow, $"Use {item.Name}", ScreenChromeKit.AccentColor,
                    true, GlassPanelMaterials.Style.Button, width: 0f, height: 44f, fontSize: 14, stretchWidth: true);
                var captured = item;
                button.onClick.AddListener(() => onUseItem?.Invoke(captured));
            }
        }

        private void BuildUI()
        {
            var (_, canvasRect) = ScreenChromeKit.CreateCanvas(transform, "EngagementCanvas", SortingOrder);
            background = ScreenChromeKit.CreateOpaqueBackground(canvasRect);

            // Tighter spacing and vertical padding than CreateGlassPanel's
            // defaults (20f spacing, 28f all round) — this panel stacks
            // far more children than any other glass panel in the game
            // (two portraits, up to 4 stat rows, dividers, banners,
            // action row), so the usual defaults were the other
            // contributor to the panel overflowing the reference canvas
            // height alongside the portrait size above. Horizontal
            // padding is left at the default (doesn't affect vertical
            // overflow and the panel's width is already tight against
            // its content).
            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor,
                spacing: 8f, padding: new RectOffset(28, 28, 18, 18));
            panelRect = rect;
            panelMaterial = material;

            // A colored banner strip up top, tinted to the active
            // player's color in Refresh — same "identity banner" touch
            // MatchHudChrome's own accentBar gives the in-match HUD.
            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();

            headerText = CreatePanelText(panel.transform, string.Empty, fontSize: 24, bold: true);
            flavorText = CreatePanelText(panel.transform, string.Empty, fontSize: 13);
            flavorText.fontStyle = FontStyle.Italic;

            CreateDivider(panel.transform);

            // Battle scene: opponent faces down (rotated from its sprite's
            // nose-up rest pose, see ShipMarkerView), player faces up —
            // framed as a face-off across the VS divider below, not two
            // independently-oriented icons.
            opponentNameText = CreatePanelText(panel.transform, string.Empty, fontSize: 16, bold: true);
            opponentAccentUnderline = CreateAccentUnderline(panel.transform);
            var (opponentPortraitObj, opponentPortraitImg, opponentFlashImg) = CreatePortraitBlock(panel.transform, faceDown: true);
            opponentPortraitRect = opponentPortraitObj;
            opponentPortrait = opponentPortraitImg;
            opponentPortraitFlash = opponentFlashImg;

            var (_, opponentHullFillImage, opponentHullValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Hull, "Hull");
            opponentHullFill = opponentHullFillImage;
            opponentHullValueText = opponentHullValue;

            var (energyRow, opponentEnergyFillImage, opponentEnergyValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Energy, "Nrg");
            opponentEnergyRow = energyRow;
            opponentEnergyFill = opponentEnergyFillImage;
            opponentEnergyValueText = opponentEnergyValue;

            var opponentStatsRow = CreateStatRow(panel.transform);
            opponentWeaponsValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn", valueFontSize: 14);
            opponentShieldsValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Shields, "Shld", valueFontSize: 14);
            opponentSpeedValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Speed, "Spd", valueFontSize: 14);

            CreateVersusDivider(panel.transform);

            var (playerPortraitObj, playerPortraitImg, playerFlashImg) = CreatePortraitBlock(panel.transform, faceDown: false);
            playerPortraitRect = playerPortraitObj;
            playerPortrait = playerPortraitImg;
            playerPortraitFlash = playerFlashImg;
            playerNameText = CreatePanelText(panel.transform, "You", fontSize: 16, bold: true);
            playerAccentUnderline = CreateAccentUnderline(panel.transform);

            var (_, playerHullFillImage, playerHullValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Hull, "Hull");
            playerHullFill = playerHullFillImage;
            playerHullValueText = playerHullValue;

            var (_, playerEnergyFillImage, playerEnergyValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Energy, "Nrg");
            playerEnergyFill = playerEnergyFillImage;
            playerEnergyValueText = playerEnergyValue;

            var playerStatsRow = CreateStatRow(panel.transform);
            playerWeaponsValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn", valueFontSize: 14);
            playerShieldsValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Shields, "Shld", valueFontSize: 14);
            playerSpeedValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Speed, "Spd", valueFontSize: 14);

            CreateDivider(panel.transform);

            // The prominent "what just happened" banner. Sits above the
            // action area so it's the first thing seen after any round
            // resolves, before the next choice's buttons.
            roundResultText = CreatePanelText(panel.transform, string.Empty, fontSize: 18, bold: true);
            SetPanelTextVisible(roundResultText, false);

            // The actual roll numbers behind that headline — icon +
            // roll+stat=total per side (see RollDetailInfo/CreateRollDetailRow),
            // sized and positioned to actually be seen (unlike the old
            // bottom log, which this replaces entirely rather than
            // duplicates). Always structurally present (faded via alpha
            // when empty, not SetActive-toggled — see CreateRollDetailRow's
            // own comment) rather than appearing/disappearing like the
            // Text lines around it.
            (rollDetailPlayerIcon, rollDetailPlayerFormulaText, rollDetailVsText, rollDetailOpponentIcon, rollDetailOpponentFormulaText) = CreateRollDetailRow(panel.transform);

            // Item-use feedback only now ("Used {item}.") — see this
            // field's own comment in the field block above.
            rollDetailText = CreatePanelText(panel.transform, string.Empty, fontSize: 14);
            rollDetailText.color = new Color(1f, 1f, 1f, 0.8f);
            SetPanelTextVisible(rollDetailText, false);

            actionInstructionText = CreatePanelText(panel.transform, string.Empty, fontSize: 14);

            var actionRowObject = new GameObject("ActionRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            actionRowObject.transform.SetParent(panel.transform, false);
            var actionRowLayout = actionRowObject.GetComponent<HorizontalLayoutGroup>();
            actionRowLayout.spacing = ActionRowSpacing;
            actionRowLayout.childAlignment = TextAnchor.MiddleCenter;
            actionRowLayout.childForceExpandWidth = false;
            actionRowLayout.childForceExpandHeight = false;
            actionRowLayout.childControlWidth = false;
            actionRowLayout.childControlHeight = false;
            actionRowObject.GetComponent<LayoutElement>().preferredHeight = 56f;
            actionRow = actionRowObject.transform;

            var consumableRowObject = new GameObject("ConsumableRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            consumableRowObject.transform.SetParent(panel.transform, false);
            var consumableRowLayout = consumableRowObject.GetComponent<VerticalLayoutGroup>();
            consumableRowLayout.spacing = 6f;
            consumableRowLayout.childAlignment = TextAnchor.UpperCenter;
            consumableRowLayout.childForceExpandWidth = true;
            consumableRowLayout.childForceExpandHeight = false;
            consumableRowLayout.childControlWidth = true;
            consumableRowLayout.childControlHeight = true;
            consumableRow = consumableRowObject.transform;

            // Added last so it's the LAST sibling under background — UGUI
            // draws later siblings on top, so the shot always renders
            // above the panel (and everything in it) regardless of where
            // it's travelling. Full-screen and non-layout (plain anchor
            // stretch, no LayoutGroup) so nothing tries to reposition or
            // resize it out from under the animation.
            var effectsLayerObject = new GameObject("BattleEffectsLayer", typeof(RectTransform));
            effectsLayerObject.transform.SetParent(background.transform, false);
            var effectsLayerRect = effectsLayerObject.GetComponent<RectTransform>();
            effectsLayerRect.anchorMin = Vector2.zero;
            effectsLayerRect.anchorMax = Vector2.one;
            effectsLayerRect.offsetMin = Vector2.zero;
            effectsLayerRect.offsetMax = Vector2.zero;

            var shotObject = new GameObject("AttackShot", typeof(RectTransform), typeof(Image));
            shotObject.transform.SetParent(effectsLayerRect, false);
            attackShotRect = shotObject.GetComponent<RectTransform>();
            attackShotRect.sizeDelta = new Vector2(7f, 30f);
            attackShotImage = shotObject.GetComponent<Image>();
            attackShotImage.color = new Color(1f, 1f, 1f, 0f);
            attackShotImage.raycastTarget = false;

            // The explosion's actual burst shape — a procedural starburst
            // texture (not a plain rectangle), same "generate it once at
            // runtime, no imported asset" approach ShipMarkerView's engine
            // glow already uses. White tint so the texture's own baked
            // white-to-orange gradient shows through unmodified; only
            // alpha/scale are animated (see PortraitExplosionRoutine).
            var explosionObject = new GameObject("ExplosionBurst", typeof(RectTransform), typeof(Image));
            explosionObject.transform.SetParent(effectsLayerRect, false);
            explosionBurstRect = explosionObject.GetComponent<RectTransform>();
            explosionBurstRect.sizeDelta = new Vector2(ExplosionVisualSize, ExplosionVisualSize);
            explosionBurstImage = explosionObject.GetComponent<Image>();
            explosionBurstImage.sprite = GetOrCreateExplosionBurstSprite();
            explosionBurstImage.color = new Color(1f, 1f, 1f, 0f);
            explosionBurstImage.raycastTarget = false;

            // A handful of small embers flung outward alongside the main
            // burst — reuses the same starburst texture at a tiny scale
            // (just reads as a bright warm dot at that size, which is all
            // a scattering spark needs to be).
            emberRects = new RectTransform[EmberCount];
            emberImages = new Image[EmberCount];
            for (var i = 0; i < EmberCount; i++)
            {
                var emberObject = new GameObject($"Ember{i}", typeof(RectTransform), typeof(Image));
                emberObject.transform.SetParent(effectsLayerRect, false);
                var emberRect = emberObject.GetComponent<RectTransform>();
                emberRect.sizeDelta = new Vector2(EmberSize, EmberSize);
                var emberImage = emberObject.GetComponent<Image>();
                emberImage.sprite = GetOrCreateExplosionBurstSprite();
                emberImage.color = new Color(1f, 1f, 1f, 0f);
                emberImage.raycastTarget = false;
                emberRects[i] = emberRect;
                emberImages[i] = emberImage;
            }

            // The ship itself breaking into 4 quadrant pieces during the
            // killing-blow explosion. Each cell is a RectMask2D-clipped
            // window; its child Content image is the SAME ship sprite at
            // full portrait size, offset by the NEGATIVE of the cell's
            // own quadrant offset — so at rest, combined with the cell's
            // own position, the content always lands exactly back at the
            // true portrait center regardless of which quadrant the cell
            // represents, and only that quadrant peeks through the mask.
            // Moving the cell (not the content) during the explosion
            // carries that captured quadrant away as one rigid piece.
            var shardsRootObject = new GameObject("ShipShardsRoot", typeof(RectTransform));
            shardsRootObject.transform.SetParent(effectsLayerRect, false);
            shipShardsRoot = shardsRootObject.GetComponent<RectTransform>();

            shipShardCellRects = new RectTransform[ShipShardCount];
            shipShardInnerImages = new Image[ShipShardCount];
            for (var i = 0; i < ShipShardCount; i++)
            {
                var cellObject = new GameObject($"ShipShard{i}", typeof(RectTransform), typeof(RectMask2D));
                cellObject.transform.SetParent(shipShardsRoot, false);
                var cellRect = cellObject.GetComponent<RectTransform>();
                cellRect.sizeDelta = new Vector2(ShipShardCellSize, ShipShardCellSize);
                cellRect.anchoredPosition = ShipShardQuadrantOffsets[i];

                var contentObject = new GameObject("Content", typeof(RectTransform), typeof(Image));
                contentObject.transform.SetParent(cellObject.transform, false);
                var contentRect = contentObject.GetComponent<RectTransform>();
                contentRect.sizeDelta = new Vector2(PortraitSize, PortraitSize);
                contentRect.anchoredPosition = -ShipShardQuadrantOffsets[i];
                var contentImage = contentObject.GetComponent<Image>();
                contentImage.preserveAspect = true;
                contentImage.raycastTarget = false;
                contentImage.color = new Color(1f, 1f, 1f, 0f);

                shipShardCellRects[i] = cellRect;
                shipShardInnerImages[i] = contentImage;
            }

            // Impact sparks — a separate, smaller pool from the embers
            // above (see the field comment on impactSparkRects for why).
            impactSparkRects = new RectTransform[ImpactSparkPoolSize];
            impactSparkImages = new Image[ImpactSparkPoolSize];
            for (var i = 0; i < ImpactSparkPoolSize; i++)
            {
                var sparkObject = new GameObject($"ImpactSpark{i}", typeof(RectTransform), typeof(Image));
                sparkObject.transform.SetParent(effectsLayerRect, false);
                var sparkRect = sparkObject.GetComponent<RectTransform>();
                sparkRect.sizeDelta = new Vector2(ImpactSparkSize, ImpactSparkSize);
                var sparkImage = sparkObject.GetComponent<Image>();
                sparkImage.sprite = GetOrCreateExplosionBurstSprite();
                sparkImage.color = new Color(1f, 1f, 1f, 0f);
                sparkImage.raycastTarget = false;
                impactSparkRects[i] = sparkRect;
                impactSparkImages[i] = sparkImage;
            }

            // The shockwave ring — same starburst/ember layer, drawn
            // after the main burst so it sits on top as it races outward
            // past the burst's own edge.
            var shockwaveObject = new GameObject("ShockwaveRing", typeof(RectTransform), typeof(Image));
            shockwaveObject.transform.SetParent(effectsLayerRect, false);
            shockwaveRect = shockwaveObject.GetComponent<RectTransform>();
            shockwaveRect.sizeDelta = new Vector2(ExplosionVisualSize, ExplosionVisualSize);
            shockwaveImage = shockwaveObject.GetComponent<Image>();
            shockwaveImage.sprite = GetOrCreateShockwaveRingSprite();
            shockwaveImage.color = new Color(1f, 1f, 1f, 0f);
            shockwaveImage.raycastTarget = false;

            // The initiative-win ring — same layer, own dedicated
            // RectTransform/Image (see field comment for why it can't
            // share the shockwave's).
            var initiativeRingObject = new GameObject("InitiativeRing", typeof(RectTransform), typeof(Image));
            initiativeRingObject.transform.SetParent(effectsLayerRect, false);
            initiativeRingRect = initiativeRingObject.GetComponent<RectTransform>();
            initiativeRingRect.sizeDelta = new Vector2(InitiativeRingSize, InitiativeRingSize);
            initiativeRingImage = initiativeRingObject.GetComponent<Image>();
            initiativeRingImage.sprite = GetOrCreateShockwaveRingSprite();
            initiativeRingImage.color = new Color(1f, 1f, 1f, 0f);
            initiativeRingImage.raycastTarget = false;

            // Secondary "chain reaction" bursts — reuse the main burst's
            // own texture at a smaller size, positioned/offset per-
            // explosion in PortraitExplosionRoutine.
            secondaryBurstRects = new RectTransform[SecondaryBurstCount];
            secondaryBurstImages = new Image[SecondaryBurstCount];
            for (var i = 0; i < SecondaryBurstCount; i++)
            {
                var secondaryObject = new GameObject($"SecondaryBurst{i}", typeof(RectTransform), typeof(Image));
                secondaryObject.transform.SetParent(effectsLayerRect, false);
                var secondaryRect = secondaryObject.GetComponent<RectTransform>();
                secondaryRect.sizeDelta = new Vector2(SecondaryBurstSize, SecondaryBurstSize);
                var secondaryImage = secondaryObject.GetComponent<Image>();
                secondaryImage.sprite = GetOrCreateExplosionBurstSprite();
                secondaryImage.color = new Color(1f, 1f, 1f, 0f);
                secondaryImage.raycastTarget = false;
                secondaryBurstRects[i] = secondaryRect;
                secondaryBurstImages[i] = secondaryImage;
            }

            CreateRollRevealModal(effectsLayerRect);
        }

        // The dice-roll reveal modal — see the constants block near
        // RollBadgeTextureSize for the full reasoning. A dimmed full-
        // screen backdrop behind a glass-styled card, anchored dead
        // center ONCE here and never repositioned again — PlayRollRevealRoutine
        // only ever touches the card's CONTENT and its CanvasGroup/local-
        // scale animation, never its position.
        private void CreateRollRevealModal(Transform parent)
        {
            var backdropObject = new GameObject("RollRevealBackdrop", typeof(RectTransform), typeof(Image));
            backdropObject.transform.SetParent(parent, false);
            var backdropRect = backdropObject.GetComponent<RectTransform>();
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;
            rollRevealBackdropImage = backdropObject.GetComponent<Image>();
            rollRevealBackdropImage.color = new Color(0f, 0f, 0f, 0f);
            rollRevealBackdropImage.raycastTarget = false;

            // Same glass-panel visual language the main panel itself
            // uses (GlassPanelMaterials/SyncPanelSize — see LateUpdate)
            // so this reads as a native part of this screen's own design
            // system rather than an ad-hoc overlay.
            var cardObject = new GameObject("RollRevealCard", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(HorizontalLayoutGroup));
            cardObject.transform.SetParent(parent, false);
            rollRevealCardRect = cardObject.GetComponent<RectTransform>();
            rollRevealCardRect.anchorMin = new Vector2(0.5f, 0.5f);
            rollRevealCardRect.anchorMax = new Vector2(0.5f, 0.5f);
            rollRevealCardRect.pivot = new Vector2(0.5f, 0.5f);
            rollRevealCardRect.sizeDelta = new Vector2(RollCardWidth, RollCardHeight);
            rollRevealCardMaterial = GlassPanelMaterials.Create(GlassPanelMaterials.Style.Panel, VersusColor);
            var cardImage = cardObject.GetComponent<Image>();
            cardImage.material = rollRevealCardMaterial;
            cardImage.raycastTarget = false;
            rollRevealCardGroup = cardObject.GetComponent<CanvasGroup>();
            rollRevealCardGroup.alpha = 0f;
            rollRevealCardGroup.blocksRaycasts = false;
            rollRevealCardGroup.interactable = false;

            var cardLayout = cardObject.GetComponent<HorizontalLayoutGroup>();
            cardLayout.padding = new RectOffset(18, 18, 14, 14);
            cardLayout.spacing = 10f;
            cardLayout.childAlignment = TextAnchor.MiddleCenter;
            cardLayout.childForceExpandWidth = true;
            cardLayout.childForceExpandHeight = true;
            cardLayout.childControlWidth = true;
            cardLayout.childControlHeight = true;

            (playerRollBadge, playerRollBadgeGlow, playerRollNumberText, playerRollMathIcon, playerRollMathText) = CreateRollRevealColumn(cardObject.transform);

            var vsText = ScreenChromeKit.CreateText(cardObject.transform, "VS", fontSize: 16, bold: true);
            vsText.color = VersusColor;
            var vsLayoutElement = vsText.GetComponent<LayoutElement>();
            vsLayoutElement.flexibleWidth = 0f;
            vsLayoutElement.preferredWidth = 28f;

            (opponentRollBadge, opponentRollBadgeGlow, opponentRollNumberText, opponentRollMathIcon, opponentRollMathText) = CreateRollRevealColumn(cardObject.transform);
        }

        // One side of the card — a badge with its roll number as a
        // CHILD (anchor-stretched inside it, same pattern CreatePortraitBlock's
        // Sprite/ImpactFlash children already use) so the two move/scale
        // together as one layout cell, plus an icon+math row below it.
        private static (Image Badge, Image Glow, Text Number, Image MathIcon, Text Math) CreateRollRevealColumn(Transform parent)
        {
            var columnObject = new GameObject("RollRevealColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            columnObject.transform.SetParent(parent, false);
            var columnLayout = columnObject.GetComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 8f;
            columnLayout.childAlignment = TextAnchor.MiddleCenter;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;

            var badgeObject = new GameObject("RollBadge", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            badgeObject.transform.SetParent(columnObject.transform, false);
            var badgeLayoutElement = badgeObject.GetComponent<LayoutElement>();
            badgeLayoutElement.preferredWidth = RollBadgeSize;
            badgeLayoutElement.preferredHeight = RollBadgeSize;
            badgeLayoutElement.flexibleWidth = 0f;
            var badgeImage = badgeObject.GetComponent<Image>();
            badgeImage.sprite = GetOrCreateRollBadgeSprite();
            badgeImage.raycastTarget = false;

            // Winner-emphasis halo (see RollRevealWinnerEmphasisDuration) —
            // a child of the badge itself, not the column, so it's free
            // of the VerticalLayoutGroup entirely and can overhang the
            // badge's own edges via negative offsets. Created before the
            // number so it renders behind the number but in front of the
            // plain badge circle.
            var glowObject = new GameObject("RollBadgeGlow", typeof(RectTransform), typeof(Image));
            glowObject.transform.SetParent(badgeObject.transform, false);
            var glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.anchorMin = Vector2.zero;
            glowRect.anchorMax = Vector2.one;
            glowRect.offsetMin = new Vector2(-RollBadgeGlowPadding, -RollBadgeGlowPadding);
            glowRect.offsetMax = new Vector2(RollBadgeGlowPadding, RollBadgeGlowPadding);
            var glowImage = glowObject.GetComponent<Image>();
            glowImage.sprite = GetOrCreateRollBadgeSprite();
            glowImage.color = new Color(1f, 1f, 1f, 0f);
            glowImage.raycastTarget = false;

            var numberObject = new GameObject("RollNumber", typeof(RectTransform), typeof(Text));
            numberObject.transform.SetParent(badgeObject.transform, false);
            var numberRect = numberObject.GetComponent<RectTransform>();
            numberRect.anchorMin = Vector2.zero;
            numberRect.anchorMax = Vector2.one;
            numberRect.offsetMin = Vector2.zero;
            numberRect.offsetMax = Vector2.zero;
            var numberText = numberObject.GetComponent<Text>();
            numberText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            numberText.fontSize = 24;
            numberText.fontStyle = FontStyle.Bold;
            numberText.alignment = TextAnchor.MiddleCenter;
            numberText.color = Color.white;
            numberText.raycastTarget = false;

            // Which stat is actually being added — same glyph the
            // settled compact row (RollDetailInfo) already shows, so the
            // "+3" here always carries its own context instead of being
            // an unlabeled number.
            var mathRowObject = new GameObject("RollMathRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            mathRowObject.transform.SetParent(columnObject.transform, false);
            var mathRowLayout = mathRowObject.GetComponent<HorizontalLayoutGroup>();
            mathRowLayout.spacing = 4f;
            mathRowLayout.childAlignment = TextAnchor.MiddleCenter;
            mathRowLayout.childForceExpandWidth = false;
            mathRowLayout.childForceExpandHeight = false;
            mathRowLayout.childControlWidth = true;
            mathRowLayout.childControlHeight = true;

            var mathIconObject = new GameObject("RollMathIcon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            mathIconObject.transform.SetParent(mathRowObject.transform, false);
            var mathIconLayoutElement = mathIconObject.GetComponent<LayoutElement>();
            mathIconLayoutElement.preferredWidth = RollMathIconSize;
            mathIconLayoutElement.preferredHeight = RollMathIconSize;
            var mathIconImage = mathIconObject.GetComponent<Image>();
            mathIconImage.raycastTarget = false;
            // Same per-instance-material reasoning as CreateRollDetailSide.
            mathIconImage.material = new Material(Shader.Find("StarBound/IconGlyph"));

            var mathText = ScreenChromeKit.CreateText(mathRowObject.transform, string.Empty, fontSize: 14, bold: true);
            mathText.GetComponent<LayoutElement>().flexibleWidth = 0f;

            return (badgeImage, glowImage, numberText, mathIconImage, mathText);
        }

        // A jagged radial starburst, generated once and cached — angular
        // "wedge" technique (ray count modulating how far the silhouette
        // reaches at each angle) rather than a plain circle, same spirit
        // as the hex-shader rib/facet effects elsewhere in this project,
        // just computed directly in C# since this is a one-time runtime
        // texture rather than a per-frame shader. White-hot core fading
        // through orange toward the jagged edge.
        private static Sprite explosionBurstSprite;

        private static Sprite GetOrCreateExplosionBurstSprite()
        {
            if (explosionBurstSprite != null)
                return explosionBurstSprite;

            var texture = new Texture2D(ExplosionTextureSize, ExplosionTextureSize, TextureFormat.RGBA32, false)
            {
                name = "ProceduralExplosionBurst",
                wrapMode = TextureWrapMode.Clamp
            };

            var center = (ExplosionTextureSize - 1) / 2f;
            var maxRadius = center * 0.95f;

            for (var y = 0; y < ExplosionTextureSize; y++)
            {
                for (var x = 0; x < ExplosionTextureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var angle = Mathf.Atan2(dy, dx);

                    var spike = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(angle * ExplosionRayCount)), 2f);
                    var edgeRadius = maxRadius * Mathf.Lerp(0.35f, 1f, spike);

                    var alpha = Mathf.Pow(1f - Mathf.Clamp01(dist / edgeRadius), 1.3f);
                    var core = 1f - Mathf.Clamp01(dist / (maxRadius * 0.25f));
                    var g = Mathf.Lerp(0.55f, 1f, core);
                    var b = Mathf.Lerp(0.15f, 0.85f, core);

                    texture.SetPixel(x, y, new Color(1f, g, b, alpha));
                }
            }

            texture.Apply();
            explosionBurstSprite = Sprite.Create(texture, new Rect(0, 0, ExplosionTextureSize, ExplosionTextureSize),
                new Vector2(0.5f, 0.5f), ExplosionTextureSize);
            return explosionBurstSprite;
        }

        // A soft, smoothly-falling-off radial glow — deliberately NOT the
        // jagged starburst used for the explosion (GetOrCreateExplosion
        // BurstSprite); a landed hit should read as a quick bright pop,
        // not a small blast. Same generate-once-and-cache approach.
        private static Sprite impactFlashSprite;

        private static Sprite GetOrCreateImpactFlashSprite()
        {
            if (impactFlashSprite != null)
                return impactFlashSprite;

            var texture = new Texture2D(ImpactFlashTextureSize, ImpactFlashTextureSize, TextureFormat.RGBA32, false)
            {
                name = "ProceduralImpactFlash",
                wrapMode = TextureWrapMode.Clamp
            };

            var center = (ImpactFlashTextureSize - 1) / 2f;
            var maxRadius = center * 0.95f;

            for (var y = 0; y < ImpactFlashTextureSize; y++)
            {
                for (var x = 0; x < ImpactFlashTextureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var normDist = Mathf.Clamp01(dist / maxRadius);

                    var alpha = Mathf.Pow(1f - normDist, 2.2f);
                    var g = Mathf.Lerp(0.6f, 1f, 1f - normDist);
                    var b = Mathf.Lerp(0.2f, 0.85f, 1f - normDist);

                    texture.SetPixel(x, y, new Color(1f, g, b, alpha));
                }
            }

            texture.Apply();
            impactFlashSprite = Sprite.Create(texture, new Rect(0, 0, ImpactFlashTextureSize, ImpactFlashTextureSize),
                new Vector2(0.5f, 0.5f), ImpactFlashTextureSize);
            return impactFlashSprite;
        }

        // A plain white disc with a thin antialiased edge — the roll-reveal
        // duel's badge backdrop (see PlayRollRevealRoutine). Deliberately
        // NOT tinted in the texture itself, unlike the explosion/impact-
        // flash sprites above (which bake in their own warm gradient) —
        // this one is tinted per-side at runtime via Image.color
        // (player/opponent accent color), the same color-coding
        // convention already used everywhere else on this screen, so a
        // single shared sprite works for both sides.
        private static Sprite rollBadgeSprite;

        private static Sprite GetOrCreateRollBadgeSprite()
        {
            if (rollBadgeSprite != null)
                return rollBadgeSprite;

            var texture = new Texture2D(RollBadgeTextureSize, RollBadgeTextureSize, TextureFormat.RGBA32, false)
            {
                name = "ProceduralRollBadge",
                wrapMode = TextureWrapMode.Clamp
            };

            var center = (RollBadgeTextureSize - 1) / 2f;
            var maxRadius = center * 0.95f;

            for (var y = 0; y < RollBadgeTextureSize; y++)
            {
                for (var x = 0; x < RollBadgeTextureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    // Solid out to ~85% of the radius, then a short, crisp
                    // falloff to the edge — reads as a disc/badge, not a
                    // soft glow.
                    var alpha = Mathf.Clamp01((maxRadius - dist) / (maxRadius * 0.15f));
                    texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }

            texture.Apply();
            rollBadgeSprite = Sprite.Create(texture, new Rect(0, 0, RollBadgeTextureSize, RollBadgeTextureSize),
                new Vector2(0.5f, 0.5f), RollBadgeTextureSize);
            return rollBadgeSprite;
        }

        // A plain opaque white 1x1 — exists solely so the stat bar fill
        // Image (see CreateStatBarRow) has a non-null sprite. Image.
        // Type.Filled is silently ignored without one (see the comment
        // there) regardless of any other setting.
        private static Sprite whiteFillSprite;

        private static Sprite GetOrCreateWhiteFillSprite()
        {
            if (whiteFillSprite != null)
                return whiteFillSprite;

            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false) { name = "WhiteFill" };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            whiteFillSprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f));
            return whiteFillSprite;
        }

        // A thin annulus rather than a filled disc — a bright band near
        // the outer edge (normDist ~0.78) fading sharply to transparent
        // on both sides of it, so this reads as a shockwave front, not a
        // second copy of the main starburst. Same generate-once-and-
        // cache approach as GetOrCreateExplosionBurstSprite.
        private static Sprite shockwaveRingSprite;

        private static Sprite GetOrCreateShockwaveRingSprite()
        {
            if (shockwaveRingSprite != null)
                return shockwaveRingSprite;

            var texture = new Texture2D(ExplosionTextureSize, ExplosionTextureSize, TextureFormat.RGBA32, false)
            {
                name = "ProceduralShockwaveRing",
                wrapMode = TextureWrapMode.Clamp
            };

            var center = (ExplosionTextureSize - 1) / 2f;
            var maxRadius = center * 0.95f;

            for (var y = 0; y < ExplosionTextureSize; y++)
            {
                for (var x = 0; x < ExplosionTextureSize; x++)
                {
                    var dx = x - center;
                    var dy = y - center;
                    var dist = Mathf.Sqrt(dx * dx + dy * dy);
                    var normDist = dist / maxRadius;

                    var band = 1f - Mathf.Abs(normDist - 0.78f) / 0.22f;
                    var alpha = Mathf.Pow(Mathf.Clamp01(band), 1.5f);

                    texture.SetPixel(x, y, new Color(1f, 0.85f, 0.55f, alpha));
                }
            }

            texture.Apply();
            shockwaveRingSprite = Sprite.Create(texture, new Rect(0, 0, ExplosionTextureSize, ExplosionTextureSize),
                new Vector2(0.5f, 0.5f), ExplosionTextureSize);
            return shockwaveRingSprite;
        }

        // Every long/variable-length Text directly under the panel needs
        // this rather than being placed as a bare Text child of the
        // panel's own VerticalLayoutGroup. First attempt at this fix just
        // pinned an explicit LayoutElement.preferredWidth on the bare
        // Text (reasoning: ScreenChromeKit.CreateText's own LayoutElement
        // is left fully unset, and Unity's LayoutUtility.GetLayoutProperty
        // skips a higher-priority component that returns a negative
        // value rather than using it as "defer to the group," falling
        // through to Text's OWN ILayoutElement.preferredWidth — the
        // text's UNWRAPPED single-line width, enormous for a long
        // sentence) — that still weren't reliably constrained in
        // practice. This wraps the Text in its own VerticalLayoutGroup
        // container instead, mirroring CreateStatBarRow's column/row
        // wrappers, which size correctly inside this same panel: the
        // WRAPPER (not a bare Text) is what the panel's own layout group
        // directly controls, and a wrapper with no Text component on it
        // has no competing large preferredWidth to fall back to. The
        // inner Text still gets an explicit preferredWidth pinned too,
        // belt-and-suspenders, so the fix holds even if only one of the
        // two mechanisms actually matters.
        private static Text CreatePanelText(Transform parent, string content, int fontSize, bool bold = false, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var wrapObject = new GameObject("TextWrap", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            wrapObject.transform.SetParent(parent, false);
            var wrapLayout = wrapObject.GetComponent<VerticalLayoutGroup>();
            wrapLayout.padding = new RectOffset(0, 0, 0, 0);
            wrapLayout.childAlignment = TextAnchor.MiddleCenter;
            wrapLayout.childForceExpandWidth = true;
            wrapLayout.childForceExpandHeight = false;
            wrapLayout.childControlWidth = true;
            wrapLayout.childControlHeight = true;
            wrapObject.GetComponent<LayoutElement>().preferredWidth = PanelContentWidth;

            var text = ScreenChromeKit.CreateText(wrapObject.transform, content, fontSize, bold, alignment);
            text.GetComponent<LayoutElement>().preferredWidth = PanelContentWidth;
            return text;
        }

        // Toggles Text.enabled rather than any GameObject's active state
        // (the wrapper's OR the Text's own) — this was the actual root
        // cause of a text line occasionally rendering overflowed/
        // mispositioned the very first time it appeared each fight
        // (every reported case was the fight's first round-outcome,
        // exactly the one moment a line's wrapper would otherwise
        // transition inactive -> active in the same frame its text is
        // set and (for roundResultText) its shake/pulse animation
        // starts). SetActive(true) on a freshly-reactivated layout
        // subtree doesn't guarantee the layout has settled before a
        // same-frame script write lands on it — a known Unity trap.
        // Text.enabled achieves the exact same "collapses to zero space
        // when hidden" sizing behavior (Unity's layout system skips any
        // disabled Behaviour-based ILayoutElement the same way it skips
        // an inactive GameObject — see LayoutUtility.GetLayoutProperty),
        // but the wrapper GameObject itself, and its LayoutGroup
        // component, never leave the active hierarchy, so there's no
        // OnEnable/layout-dirty churn to race against in the first
        // place.
        private static void SetPanelTextVisible(Text text, bool visible) =>
            text.enabled = visible;

        // Used by the roll-math row's icons/"vs" label (see
        // CreateRollDetailRow) to fade in/out without ever toggling
        // active state — same reasoning as SetPanelTextVisible just
        // above, and the row's own formula Text elements already go
        // invisible for free when their content is set to "".
        private static void SetGraphicAlpha(Graphic graphic, float alpha)
        {
            var color = graphic.color;
            color.a = alpha;
            graphic.color = color;
        }

        // Sets both which glyph an icon shows AND which color it's
        // tinted, on that icon's own per-instance IconGlyph material
        // (assigned once at creation — see CreateRollDetailSide/
        // CreateRollRevealColumn). Alpha is deliberately left at 1 here:
        // _Color.a also feeds the shader's finalAlpha alongside
        // Image.color.a, so clobbering it would fight SetGraphicAlpha's
        // own fade-in/out of these same icons.
        //
        // Tints _GlowColor too, not just _Color — the fragment shader
        // blends glyphColor = _Color.rgb * core + _GlowColor.rgb * halo
        // * _GlowIntensity * 0.5, and at this icon's on-screen size the
        // halo term covers enough of the visible shape that leaving
        // _GlowColor at its default cool blue still read as "still
        // blue" even with _Color correctly set to the player's own
        // color — tinting only half the blend wasn't enough to actually
        // look tinted.
        private static void SetGlyphIcon(Image icon, IconGlyphMaterials.Glyph glyph, Color tint)
        {
            var solidTint = new Color(tint.r, tint.g, tint.b, 1f);
            icon.material.SetFloat("_Glyph", (float)glyph);
            icon.material.SetColor("_Color", solidTint);
            icon.material.SetColor("_GlowColor", solidTint);
        }

        // The battle-scene centerpiece: a fixed-size square holding the
        // ship sprite (Refresh assigns which one + its tint) plus a
        // same-size, fully-transparent-until-animated flash Image
        // stacked directly on top of it (see PlayPortraitImpact). Faces
        // down for the opponent (rotated 180 from the sprite's nose-up
        // rest pose — see ShipMarkerView) so the two ships visually
        // square off across the VS divider between them.
        private static (RectTransform Portrait, Image Sprite, Image Flash) CreatePortraitBlock(Transform parent, bool faceDown)
        {
            var portraitObject = new GameObject("Portrait", typeof(RectTransform), typeof(LayoutElement));
            portraitObject.transform.SetParent(parent, false);
            var layoutElement = portraitObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = PortraitSize;
            layoutElement.preferredHeight = PortraitSize;
            var portraitRect = portraitObject.GetComponent<RectTransform>();
            if (faceDown)
                portraitRect.localEulerAngles = new Vector3(0f, 0f, 180f);

            var spriteObject = new GameObject("Sprite", typeof(RectTransform), typeof(Image));
            spriteObject.transform.SetParent(portraitObject.transform, false);
            var spriteRect = spriteObject.GetComponent<RectTransform>();
            spriteRect.anchorMin = Vector2.zero;
            spriteRect.anchorMax = Vector2.one;
            spriteRect.offsetMin = Vector2.zero;
            spriteRect.offsetMax = Vector2.zero;
            var spriteImage = spriteObject.GetComponent<Image>();
            spriteImage.preserveAspect = true;
            spriteImage.raycastTarget = false;

            var flashObject = new GameObject("ImpactFlash", typeof(RectTransform), typeof(Image));
            flashObject.transform.SetParent(portraitObject.transform, false);
            var flashRect = flashObject.GetComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = Vector2.zero;
            flashRect.offsetMax = Vector2.zero;
            var flashImage = flashObject.GetComponent<Image>();
            flashImage.sprite = GetOrCreateImpactFlashSprite();
            flashImage.color = new Color(1f, 1f, 1f, 0f);
            flashImage.raycastTarget = false;

            return (portraitRect, spriteImage, flashImage);
        }

        // Label + icon + fillable bar + numeric value — same label-above
        // convention CreateIconStat already uses elsewhere (icon-only
        // stats didn't reliably read on their own, confirmed repeatedly
        // this project), generalized to whichever stat needs a bar
        // instead of a plain value (Hull/Energy here). Not added to
        // ScreenChromeKit since nothing else needs a bar yet.
        // Image.Type.Filled/Horizontal is UGUI's built-in fill
        // mechanism, no custom shader needed. The bar itself is
        // flexible-width (grows to fill whatever space is left after the
        // fixed-size icon and value), so the row spans the panel's full
        // content width instead of sitting as a small fixed-width bar
        // stranded in a lot of empty space next to it.
        // Houses the Weapons/Shields/Speed icon-stat triplet (see
        // ScreenChromeKit.CreateIconStat) below each side's Hull/Energy
        // bars — same horizontal-row pattern TurnHandoffScreen already
        // uses for its own stat line (CreateStatRow there), just local
        // to this file since nothing else here needs it.
        private static Transform CreateStatRow(Transform parent)
        {
            var rowObject = new GameObject("StatRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 12f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            return rowObject.transform;
        }

        private static (GameObject Row, Image Fill, Text Value) CreateStatBarRow(Transform parent, IconGlyphMaterials.Glyph glyph, string label)
        {
            var columnObject = new GameObject(glyph + "BarColumn", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
            columnObject.transform.SetParent(parent, false);
            var columnLayout = columnObject.GetComponent<VerticalLayoutGroup>();
            columnLayout.spacing = 2f;
            columnLayout.childAlignment = TextAnchor.UpperLeft;
            columnLayout.childForceExpandWidth = true;
            columnLayout.childForceExpandHeight = false;
            columnLayout.childControlWidth = true;
            columnLayout.childControlHeight = true;

            var labelText = ScreenChromeKit.CreateText(columnObject.transform, label, fontSize: 10, alignment: TextAnchor.MiddleLeft);
            labelText.color = new Color(1f, 1f, 1f, 0.65f);

            var row = new GameObject(glyph + "BarRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(columnObject.transform, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8f;
            rowLayout.childAlignment = TextAnchor.MiddleLeft;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(row.transform, false);
            var iconLayoutElement = iconObject.GetComponent<LayoutElement>();
            iconLayoutElement.preferredWidth = 20f;
            iconLayoutElement.preferredHeight = 20f;
            var iconImage = iconObject.GetComponent<Image>();
            iconImage.material = IconGlyphMaterials.Get(glyph);
            iconImage.raycastTarget = false;

            var trackObject = new GameObject(glyph + "BarTrack", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            trackObject.transform.SetParent(row.transform, false);
            var trackLayoutElement = trackObject.GetComponent<LayoutElement>();
            trackLayoutElement.preferredWidth = StatBarMinWidth;
            trackLayoutElement.flexibleWidth = 1f;
            trackLayoutElement.preferredHeight = 14f;
            // Dark rather than faint white — fillImage.fillAmount DOES
            // shrink correctly as a stat drops (confirmed: Image.Type.
            // Filled/Horizontal, fraction-driven), but at the old
            // alpha=0.12 the empty remainder was nearly invisible against
            // the panel background, so the shrink itself didn't read.
            // A dark, clearly-visible track makes the green/red fill's
            // retreat obvious instead of just a color change.
            trackObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.4f);

            var fillObject = new GameObject(glyph + "BarFill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(trackObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillObject.GetComponent<Image>();
            // The actual root cause of bars not visually shrinking: Image.
            // OnPopulateMesh falls back to drawing a plain full-rect quad
            // — ignoring Type/fillAmount entirely — whenever Image.sprite
            // is null. Type.Filled/fillMethod/fillOrigin below were
            // always correct; fillAmount was being computed correctly in
            // SetStatBar too. Without a sprite assigned, none of it had
            // any effect and the bar always rendered as 100% full,
            // regardless of fillAmount — exactly what was observed.
            fillImage.sprite = GetOrCreateWhiteFillSprite();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.raycastTarget = false;

            var valueText = ScreenChromeKit.CreateText(row.transform, string.Empty, fontSize: 15, bold: true);
            valueText.GetComponent<LayoutElement>().preferredWidth = 26f;

            return (columnObject, fillImage, valueText);
        }

        // A short colored underline centered beneath a combatant's name
        // — cheap per-side identity marker, tinted in Refresh to that
        // side's own accent color (player color in PvP, neutral accent
        // for NPCs).
        private static Image CreateAccentUnderline(Transform parent)
        {
            var lineObject = new GameObject("AccentUnderline", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            lineObject.transform.SetParent(parent, false);
            var layoutElement = lineObject.GetComponent<LayoutElement>();
            layoutElement.preferredWidth = 48f;
            layoutElement.preferredHeight = 3f;
            return lineObject.GetComponent<Image>();
        }

        // A bold centered "VS" flanked by two flexible-width lines,
        // replacing a plain divider between the two combatant blocks —
        // the one moment in the panel that's actually about confrontation,
        // so it gets more presence than a 1pt separator line.
        private static void CreateVersusDivider(Transform parent)
        {
            var row = new GameObject("VersusRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 10f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            row.GetComponent<LayoutElement>().preferredHeight = 20f;

            CreateFlexLine(row.transform);
            var vsText = ScreenChromeKit.CreateText(row.transform, "VS", fontSize: 15, bold: true);
            vsText.color = VersusColor;
            vsText.GetComponent<LayoutElement>().flexibleWidth = 0f;
            CreateFlexLine(row.transform);
        }

        private static void CreateFlexLine(Transform parent)
        {
            var lineObject = new GameObject("Line", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            lineObject.transform.SetParent(parent, false);
            var layoutElement = lineObject.GetComponent<LayoutElement>();
            layoutElement.flexibleWidth = 1f;
            layoutElement.preferredHeight = 1f;
            lineObject.GetComponent<Image>().color = DividerColor;
        }

        // The compact "[icon] roll+stat=total  vs  [icon] roll+stat=total"
        // roll-math row — replaces the old full-sentence rollDetailText
        // for attack/initiative/escape rolls (see RollDetailInfo). Same
        // direct-panel-child HorizontalLayoutGroup shape as
        // CreateVersusDivider (already proven not to overflow), just with
        // unforced child widths (childForceExpandWidth = false) so each
        // icon/formula keeps its own small natural size and the whole
        // row centers as one block, rather than stretching to fill the
        // panel's width the way CreateVersusDivider's flex-lines want to.
        // Deliberately has no label-above (unlike CreateStatBarRow) —
        // the icon itself identifies the stat, there's no bar to caption.
        private static (Image PlayerIcon, Text PlayerFormula, Text VsText, Image OpponentIcon, Text OpponentFormula) CreateRollDetailRow(Transform parent)
        {
            var row = new GameObject("RollDetailRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.transform.SetParent(parent, false);
            var rowLayout = row.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 6f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;

            var (playerIcon, playerFormula) = CreateRollDetailSide(row.transform);

            // Dim gray at FULL alpha, not white at reduced alpha — this
            // label's own alpha is what Refresh fades between 0 (nothing
            // to show yet) and 1 (a roll just happened), so the "dim"
            // look has to live in the RGB channels instead, or fading it
            // in would brighten it back toward white instead of just
            // appearing.
            var vsText = ScreenChromeKit.CreateText(row.transform, "vs", fontSize: 11);
            vsText.color = new Color(0.75f, 0.8f, 0.85f, 0f);
            vsText.GetComponent<LayoutElement>().flexibleWidth = 0f;

            var (opponentIcon, opponentFormula) = CreateRollDetailSide(row.transform);

            return (playerIcon, playerFormula, vsText, opponentIcon, opponentFormula);
        }

        private static (Image Icon, Text Formula) CreateRollDetailSide(Transform parent)
        {
            var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            iconObject.transform.SetParent(parent, false);
            var iconLayoutElement = iconObject.GetComponent<LayoutElement>();
            iconLayoutElement.preferredWidth = 18f;
            iconLayoutElement.preferredHeight = 18f;
            var iconImage = iconObject.GetComponent<Image>();
            // Starts invisible — Refresh sets the real glyph/color and
            // fades alpha to 1 once there's an actual roll to show (see
            // RollDetailInfo.HasValue); without this, a freshly built
            // screen would show two blank-but-opaque icon shapes before
            // the fight's first roll.
            iconImage.color = new Color(1f, 1f, 1f, 0f);
            iconImage.raycastTarget = false;
            // A per-INSTANCE material, assigned once here and mutated
            // in place every round after (SetFloat("_Glyph",...)/
            // SetColor("_Color",...)) — IconGlyphMaterials.Get returns a
            // material SHARED/cached across every icon of that glyph
            // game-wide (Hull/Energy bars, TurnHandoffScreen, etc.), and
            // its shader reads _Color for RGB, not Image.color, so
            // reassigning that shared material here would either do
            // nothing (RGB) or wrongly recolor every other instance of
            // that glyph anywhere else it's drawn.
            iconImage.material = new Material(Shader.Find("StarBound/IconGlyph"));

            var formulaText = ScreenChromeKit.CreateText(parent, string.Empty, fontSize: 13, bold: true);
            formulaText.GetComponent<LayoutElement>().flexibleWidth = 0f;

            return (iconImage, formulaText);
        }

        private static void CreateDivider(Transform parent)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            dividerObject.transform.SetParent(parent, false);
            dividerObject.GetComponent<LayoutElement>().preferredHeight = 1f;
            dividerObject.GetComponent<Image>().color = DividerColor;
        }
    }
}
