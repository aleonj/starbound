using System.Collections.Generic;
using StarBound.Core;

namespace StarBound.Multiplayer
{
    // Tracks one in-progress trade negotiation between the two match
    // players. Initiator/Opponent are fixed for the negotiation's whole
    // lifetime (Initiator is whoever was Match.CurrentPlayer at the
    // moment ProposeTrade was called) — the CURRENT terms are always
    // described relative to those two fixed roles, regardless of which
    // side most recently proposed them, so "InitiatorGives" always means
    // the same thing whether it's the original proposal or the one
    // allowed counter. See Match.ProposeTrade/CounterTrade/AcceptTrade.
    public class TradeNegotiation
    {
        public Player Initiator { get; }
        public Player Opponent { get; }
        public IReadOnlyList<ItemDefinition> InitiatorGives { get; private set; }
        public IReadOnlyList<ItemDefinition> OpponentGives { get; private set; }
        public int InitiatorMoney { get; private set; }
        public int OpponentMoney { get; private set; }

        // True once the Opponent's one allowed counter has been applied —
        // blocks a second counter (see Match.CounterTrade). Only the
        // Opponent ever counters; the Initiator's only options once
        // countered are Accept or Reject.
        public bool HasBeenCountered { get; private set; }

        public TradeNegotiation(
            Player initiator, Player opponent,
            IReadOnlyList<ItemDefinition> initiatorGives, IReadOnlyList<ItemDefinition> opponentGives,
            int initiatorMoney, int opponentMoney)
        {
            Initiator = initiator;
            Opponent = opponent;
            InitiatorGives = initiatorGives;
            OpponentGives = opponentGives;
            InitiatorMoney = initiatorMoney;
            OpponentMoney = opponentMoney;
        }

        // Replaces the current terms wholesale — a counter-offer isn't a
        // delta on top of the original, it's a fresh set of terms the
        // Initiator will only ever see once, as a final Accept/Reject
        // choice (see Match.CounterTrade).
        public void ApplyCounter(
            IReadOnlyList<ItemDefinition> initiatorGives, IReadOnlyList<ItemDefinition> opponentGives,
            int initiatorMoney, int opponentMoney)
        {
            InitiatorGives = initiatorGives;
            OpponentGives = opponentGives;
            InitiatorMoney = initiatorMoney;
            OpponentMoney = opponentMoney;
            HasBeenCountered = true;
        }
    }
}
