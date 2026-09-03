using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using StarBound.Combat;
using StarBound.Core;
using StarBound.Economy;

namespace StarBound.UI
{
    // Classifies a round's outcome for EngagementScreen's impact feedback
    // (see PlayRoundOutcomeFeedback) — Info covers non-attack banners
    // (Escape/Initiative), which get no impact feedback at all.
    public enum RoundBannerKind
    {
        Info,
        Miss,
        Hit,
        CriticalHit
    }


    // Full-screen, dedicated combat-resolution UI — replaces MatchHud's old
    // IMGUI DrawEngagementPanel/DrawConsumablesMidEngagement. "The most
    // frequent, highest-stakes interaction in the game" per this story,
    // so it gets the same glass/SDF treatment (via ScreenChromeKit) as the
    // other full-screen flow screens, plus a Hull/Energy "health bar" per
    // side (UGUI's native Image.Type.Filled, no custom shader).
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
        private const float StatBarMinWidth = 60f;

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

        private GameObject background;
        private RectTransform panelRect;
        private Material panelMaterial;
        private Image accentBar;
        private Text headerText;
        private Text flavorText;

        private Text opponentNameText;
        private Image opponentAccentUnderline;
        private Image opponentHullFill;
        private Text opponentHullValueText;
        private GameObject opponentEnergyRow;
        private Image opponentEnergyFill;
        private Text opponentEnergyValueText;
        private Text opponentWeaponsValueText;
        private Text opponentShieldsValueText;
        private Text opponentSpeedValueText;

        private Text playerNameText;
        private Image playerAccentUnderline;
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
        private string lastPulsedRoundResultText;
        private Text rollDetailText;

        private Text actionInstructionText;
        private Transform actionRow;
        private Transform consumableRow;

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

        private void Awake()
        {
            BuildUI();
            Hide();
        }

        private void LateUpdate() => ScreenChromeKit.SyncPanelSize(panelMaterial, panelRect);

        public void SetVisible(bool visible) => background.SetActive(visible);

        private void Hide() => SetVisible(false);

        public void Refresh(
            EngagementSession session, string opponentDisplayName, Color opponentAccentColor, Color playerAccentColor,
            Color activeAccentColor,
            string rollDetail, IReadOnlyList<ItemDefinition> usableConsumables,
            string roundResultMessage, Color roundResultColor, RoundBannerKind roundBannerKind, bool defenderIsPlayer,
            Action onAttemptEscape, Action onRollInitiative, Action onBrace, Action onHold,
            Action onAttack, Action onContinue, Action<ItemDefinition> onUseItem,
            Action onOpponentAttemptEscape, Action onOpponentBrace, Action onOpponentHold)
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
            }

            // Neutral, third-party framing ("X vs Y") rather than "vs
            // {opponent}" from the current player's own point of view —
            // once the device gets handed to the opponent mid-fight (see
            // MatchHud.HandOffDeviceTo), a header written from the OTHER
            // side's perspective would be just as wrong as "You"/
            // "opponent" log text was. This reads correctly no matter
            // who's currently holding the screen.
            headerText.text = session.IsPvP
                ? $"{session.Player.DisplayName} vs {opponentDisplayName}"
                : $"Engagement ({session.Definition.Tier})";
            headerText.color = activeAccentColor;
            panelMaterial.SetColor("_RimColor", activeAccentColor);
            accentBar.color = activeAccentColor;
            playerAccentUnderline.color = playerAccentColor;
            opponentAccentUnderline.color = opponentAccentColor;

            var showFlavor = !session.IsPvP && !string.IsNullOrEmpty(session.FlavorText);
            flavorText.gameObject.SetActive(showFlavor);
            if (showFlavor)
                flavorText.text = session.FlavorText;

            var opponentView = new OpponentStatView(session.Opponent);
            opponentNameText.text = session.IsPvP ? opponentDisplayName : "Opponent";
            opponentNameText.color = opponentAccentColor;
            SetStatBar(opponentHullFill, opponentView.Hull, opponentStartingHull);
            opponentHullValueText.text = opponentView.Hull.ToString();
            opponentWeaponsValueText.text = opponentView.Weapons.ToString();
            opponentShieldsValueText.text = opponentView.Shields.ToString();
            opponentSpeedValueText.text = opponentView.Speed.ToString();

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

            // "You" is only ever accurate from session.Player's own point
            // of view — once the device is handed to the opponent (see
            // MatchHud.HandOffDeviceTo), this panel is no longer
            // necessarily who's looking at the screen, so PvP names it
            // explicitly instead. Still "You" for NPC fights, where
            // there's no second real party to confuse it with.
            playerNameText.text = session.IsPvP ? session.Player.DisplayName : "You";
            playerNameText.color = playerAccentColor;

            var ship = session.PlayerShip;
            var playerHull = ship.GetStat(CoreStat.Hull);
            SetStatBar(playerHullFill, playerHull, playerStartingHull);
            playerHullValueText.text = playerHull.ToString();

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
            roundResultText.gameObject.SetActive(hasRoundResult);
            if (hasRoundResult)
            {
                roundResultText.text = roundResultMessage;
                roundResultText.color = roundResultColor;
                // Only fire feedback when this is actually a NEW message —
                // Refresh can run again for unrelated reasons (e.g. a
                // consumable used afterward) without re-triggering it.
                if (roundBannerKind != RoundBannerKind.Info && roundResultMessage != lastPulsedRoundResultText)
                {
                    lastPulsedRoundResultText = roundResultMessage;
                    PlayRoundOutcomeFeedback(roundBannerKind, defenderIsPlayer);
                }
            }

            // The actual roll numbers behind the headline above — the old
            // scrolling log at the bottom had this same text, but small
            // and easy to miss/scroll past. One line, most-recent-only,
            // sitting right where the player's eye already is (between
            // the outcome and the buttons) instead of competing for
            // attention at the bottom of the panel.
            rollDetailText.gameObject.SetActive(!string.IsNullOrEmpty(rollDetail));
            rollDetailText.text = rollDetail ?? string.Empty;

            RebuildActionRow(session, opponentDisplayName, opponentAccentColor, playerAccentColor,
                onAttemptEscape, onRollInitiative, onBrace, onHold, onAttack, onContinue,
                onOpponentAttemptEscape, onOpponentBrace, onOpponentHold);
            RebuildConsumableRow(usableConsumables, onUseItem);
        }

        // A miss gets its own distinct feedback (a horizontal "whiff"
        // shake, no impact at all) rather than just a quieter version of
        // a hit's — a dodge and a landed blow should read as opposite
        // things, not the same effect turned down. Hit/CriticalHit share
        // the same three effects (pulse, panel shake, defender bar
        // flash), scaled up for a crit.
        private void PlayRoundOutcomeFeedback(RoundBannerKind kind, bool defenderIsPlayer)
        {
            if (kind == RoundBannerKind.Miss)
            {
                PlayWhiffShake();
                return;
            }

            var isCrit = kind == RoundBannerKind.CriticalHit;
            PulseRoundResult(isCrit ? CritPulseScale : HitPulseScale);
            PlayPanelShake(isCrit ? CritShakeMagnitude : HitShakeMagnitude);
            PlayBarFlash(defenderIsPlayer ? playerHullFill : opponentHullFill, isCrit ? CritBarFlashDuration : HitBarFlashDuration);
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
            var elapsed = 0f;
            while (elapsed < WhiffShakeDuration)
            {
                elapsed += Time.deltaTime;
                var t = elapsed / WhiffShakeDuration;
                var offset = Mathf.Sin(t * Mathf.PI * 5f) * WhiffShakeMagnitude * (1f - t);
                rect.anchoredPosition = new Vector2(offset, rect.anchoredPosition.y);
                yield return null;
            }

            rect.anchoredPosition = new Vector2(0f, rect.anchoredPosition.y);
            whiffShakeCoroutine = null;
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
            Action onOpponentAttemptEscape, Action onOpponentBrace, Action onOpponentHold)
        {
            for (var i = actionRow.childCount - 1; i >= 0; i--)
                DestroyImmediate(actionRow.GetChild(i).gameObject);

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

            if (!session.IsAwaitingAttackResolution)
            {
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

            // Note: there's no PvP "attacker executes" state shown here —
            // once the defender declares (PvP only; see EngagementSession.
            // DeclareDefense/ExecuteAttack), MatchHud hands the device
            // straight to the attacker with an "Attack!" hand-off screen
            // whose own tap runs the roll directly (see MatchHud.
            // DeclareOrResolveDefense/OnExecuteAttackClicked) — same
            // one-tap pattern the Initiative hand-off already uses, so
            // this screen is never shown mid-way through that step.
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
            // it — MatchHud already handed the device to them (see
            // HandOffDeviceTo/AdvanceAfterInitiative) before this state is
            // ever shown.
            if (session.CanOpponentDecideDefense)
            {
                actionInstructionText.text = $"{opponentDisplayName}: escape, brace, or hold?";
                actionInstructionText.color = opponentAccentColor;

                var (opponentEscapeButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Escape",
                    session.Definition.EscapeAllowed ? ScreenChromeKit.AccentColor : ScreenChromeKit.DisabledColor,
                    session.Definition.EscapeAllowed, GlassPanelMaterials.Style.DieButton, width: ThirdActionWidth, height: 56f, fontSize: 12);
                opponentEscapeButton.onClick.AddListener(() => onOpponentAttemptEscape?.Invoke());

                var opponentCanBrace = session.Opponent.GetStat(CoreStat.Energy) > 0;
                var (opponentBraceButton, _, _) = ScreenChromeKit.CreateButton(actionRow, "Brace",
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

            var (panel, rect, material) = ScreenChromeKit.CreateGlassPanel(background.transform, PanelWidth, ScreenChromeKit.AccentColor, spacing: 10f);
            panelRect = rect;
            panelMaterial = material;

            // A colored banner strip up top, tinted to the active
            // player's color in Refresh — same "identity banner" touch
            // MatchHudChrome's own accentBar gives the in-match HUD.
            var accentBarObject = new GameObject("AccentBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            accentBarObject.transform.SetParent(panel.transform, false);
            accentBarObject.GetComponent<LayoutElement>().preferredHeight = 4f;
            accentBar = accentBarObject.GetComponent<Image>();

            headerText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 24, bold: true);
            flavorText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 13);
            flavorText.fontStyle = FontStyle.Italic;

            CreateDivider(panel.transform);

            opponentNameText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 16, bold: true);
            opponentAccentUnderline = CreateAccentUnderline(panel.transform);
            var (_, opponentHullFillImage, opponentHullValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Hull, "Hull");
            opponentHullFill = opponentHullFillImage;
            opponentHullValueText = opponentHullValue;

            var (energyRow, opponentEnergyFillImage, opponentEnergyValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Energy, "Nrg");
            opponentEnergyRow = energyRow;
            opponentEnergyFill = opponentEnergyFillImage;
            opponentEnergyValueText = opponentEnergyValue;

            var opponentStatsRow = CreateStatRow(panel.transform);
            opponentWeaponsValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn", valueFontSize: 15);
            opponentShieldsValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Shields, "Shld", valueFontSize: 15);
            opponentSpeedValueText = ScreenChromeKit.CreateIconStat(opponentStatsRow, IconGlyphMaterials.Glyph.Speed, "Spd", valueFontSize: 15);

            CreateVersusDivider(panel.transform);

            playerNameText = ScreenChromeKit.CreateText(panel.transform, "You", fontSize: 16, bold: true);
            playerAccentUnderline = CreateAccentUnderline(panel.transform);
            var (_, playerHullFillImage, playerHullValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Hull, "Hull");
            playerHullFill = playerHullFillImage;
            playerHullValueText = playerHullValue;

            var (_, playerEnergyFillImage, playerEnergyValue) = CreateStatBarRow(panel.transform, IconGlyphMaterials.Glyph.Energy, "Nrg");
            playerEnergyFill = playerEnergyFillImage;
            playerEnergyValueText = playerEnergyValue;

            var playerStatsRow = CreateStatRow(panel.transform);
            playerWeaponsValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Weapons, "Wpn", valueFontSize: 15);
            playerShieldsValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Shields, "Shld", valueFontSize: 15);
            playerSpeedValueText = ScreenChromeKit.CreateIconStat(playerStatsRow, IconGlyphMaterials.Glyph.Speed, "Spd", valueFontSize: 15);

            CreateDivider(panel.transform);

            // The prominent "what just happened" banner. Sits above the
            // action area so it's the first thing seen after any round
            // resolves, before the next choice's buttons.
            roundResultText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 18, bold: true);
            roundResultText.gameObject.SetActive(false);

            // The actual roll numbers behind that headline — smaller and
            // unbold so the headline above still reads first, but sized
            // and positioned to actually be seen (unlike the old bottom
            // log, which this replaces entirely rather than duplicates).
            rollDetailText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 14);
            rollDetailText.color = new Color(1f, 1f, 1f, 0.8f);
            rollDetailText.gameObject.SetActive(false);

            actionInstructionText = ScreenChromeKit.CreateText(panel.transform, string.Empty, fontSize: 14);

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
            trackObject.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.12f);

            var fillObject = new GameObject(glyph + "BarFill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(trackObject.transform, false);
            var fillRect = fillObject.GetComponent<RectTransform>();
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;
            var fillImage = fillObject.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
            fillImage.raycastTarget = false;

            var valueText = ScreenChromeKit.CreateText(row.transform, string.Empty, fontSize: 15, bold: true);
            valueText.GetComponent<LayoutElement>().preferredWidth = 26f;

            return (columnObject, fillImage, valueText);
        }

        // A content-sized row holding a handful of icon+value stat pairs,
        // centered as a group within the panel's full width — matches
        // the centered rhythm the name/header/VS rows already use,
        // rather than clustering at the left edge with empty space
        // trailing off to the right.
        private static Transform CreateStatRow(Transform parent)
        {
            var rowObject = new GameObject("StatRow", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            rowObject.transform.SetParent(parent, false);
            var rowLayout = rowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 14f;
            rowLayout.childAlignment = TextAnchor.MiddleCenter;
            rowLayout.childForceExpandWidth = false;
            rowLayout.childForceExpandHeight = false;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            return rowObject.transform;
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

        private static void CreateDivider(Transform parent)
        {
            var dividerObject = new GameObject("Divider", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            dividerObject.transform.SetParent(parent, false);
            dividerObject.GetComponent<LayoutElement>().preferredHeight = 1f;
            dividerObject.GetComponent<Image>().color = DividerColor;
        }
    }
}
