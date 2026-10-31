namespace SA.Balance
{
    public static class TunedParams
    {
        public static BalanceParams Tuned
        {
            get
            {
                var p = BalanceParams.Standard;
                p.FallSpeed = 3.7087204f;
                return p;
            }
        }
    }
}
