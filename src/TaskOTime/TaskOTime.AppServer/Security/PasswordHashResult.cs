namespace TaskOTime.AppServer.Security
{
    public sealed class PasswordHashResult
    {
        public PasswordHashResult(string hash, string salt)
        {
            Hash = hash;
            Salt = salt;
        }

        public string Hash { get; }

        public string Salt { get; }
    }
}
