namespace stajprojesi_uzaktan_baglanti;

// Reliable key/button/release events must never be discarded merely because an
// unordered pointer update overtook them. Older pointer positions are discarded.
internal sealed class RtcInputOrder
{
    private long reliableBarrier, lastMove;
    public bool Accept(string? action, long sequence)
    {
        if (sequence <= 0) return false;
        if (action == "hareket")
        {
            if (sequence <= reliableBarrier || sequence <= lastMove) return false;
            lastMove = sequence;
            return true;
        }
        if (action is not ("tus" or "dugme" or "tekerlek" or "birak") || sequence <= reliableBarrier) return false;
        reliableBarrier = sequence;
        return true;
    }
    public void Reset() { reliableBarrier = lastMove = 0; }
}
