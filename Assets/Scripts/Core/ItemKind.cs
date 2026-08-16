namespace StarBound.Core
{
    public enum ItemKind
    {
        // Contributes its stat bonus live for as long as it's held —
        // sell or trade it away and the bonus goes with it.
        Permanent,

        // Inert until used. Using it permanently applies its stat delta
        // to the base stat and removes the item — a used consumable
        // can't be resold or traded, unlike an unused one.
        Consumable,

        // No stat effect at all — held items of this kind gate a
        // capability (e.g. the Wormhole Device) rather than boosting a
        // CoreStat. AffectedStat/StatDelta are always null for this kind.
        Unlock
    }
}
