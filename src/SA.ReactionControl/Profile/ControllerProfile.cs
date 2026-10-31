using System.Runtime.InteropServices;
using SA.ReactionControl.Balance;
using SA.ReactionControl.Brace;
using SA.ReactionControl.CatchFall;
using SA.ReactionControl.Coordination;
using SA.ReactionControl.Drive;

namespace SA.ReactionControl.Profile
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ControllerProfile
    {
        public string Name;
        public BalanceParams Balance;
        public BodyBalanceParams BodyBalance;
        public CatchFallParams CatchFall;
        public BraceParams Brace;
        public DriveParams Drive;
        public CoordinationParams Coordination;
        public GetUpParams GetUp;
    }
}
