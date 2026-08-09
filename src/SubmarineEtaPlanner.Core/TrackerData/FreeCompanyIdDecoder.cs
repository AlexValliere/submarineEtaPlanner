using MessagePack;

namespace SubmarineEtaPlanner.TrackerData;

internal static class FreeCompanyIdDecoder
{
    public static ulong? TryDecode(ReadOnlyMemory<byte> blob)
    {
        try
        {
            return MessagePackSerializer.Deserialize<ulong>(blob);
        }
        catch
        {
            return null;
        }
    }
}
