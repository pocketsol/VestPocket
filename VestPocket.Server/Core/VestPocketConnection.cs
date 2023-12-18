namespace VestPocket.ClientServer.Core
{
    public class VestPocketConnection
    {
        public string User { get; }
        public DateTime ExpiresAt { get; }

        public VestPocketConnection(string user, DateTime expiresAt)
        {
            User = user;
            ExpiresAt = expiresAt;
        }
    }
}
