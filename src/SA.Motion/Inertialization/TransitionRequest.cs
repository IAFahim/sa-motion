using System.Runtime.InteropServices;

namespace SA.Motion.Inertialization
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RequestState
    {
        public uint Key;
        public uint Membership;
        public uint Revision;
        public byte Active;
    }

    public enum RequestDecision : byte
    {
        Start,
        Keep,
        Misuse
    }

    public static class TransitionRequests
    {
        public static RequestDecision Decide(RequestState current, uint desiredKey, uint membership, bool restart, out RequestState next)
        {
            if (current.Active == 0)
            {
                next = new RequestState { Key = desiredKey, Membership = membership, Revision = 1, Active = 1 };
                return RequestDecision.Start;
            }
            if (current.Key != desiredKey)
            {
                next = new RequestState { Key = desiredKey, Membership = membership, Revision = NextRevision(current.Revision), Active = 1 };
                return RequestDecision.Start;
            }
            if (current.Membership != membership)
            {
                next = current;
                return RequestDecision.Misuse;
            }
            if (restart)
            {
                next = current;
                next.Revision = NextRevision(current.Revision);
                return RequestDecision.Start;
            }
            next = current;
            return RequestDecision.Keep;
        }

        private static uint NextRevision(uint revision)
        {
            return revision == uint.MaxValue ? 1 : revision + 1;
        }
    }
}
