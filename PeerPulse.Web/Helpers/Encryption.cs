using System.Security.Cryptography;
using System.Text;


namespace PeerPulse.Web.Helpers
{
    public static class Encryption
    {
        public static string Create(string password, int legacyCodePage)
        {
            ArgumentNullException.ThrowIfNull(password);

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            Encoding encoding = Encoding.GetEncoding(legacyCodePage);
            byte[] bytes = encoding.GetBytes(password);

            try
            {
                byte[] hash = MD5.HashData(bytes);

                try
                {
                    return Convert.ToHexString(hash).ToLowerInvariant();
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(hash);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }

        public static bool Verify(string enteredPassword,string? databasePasswordHash,int legacyCodePage)
        {
            if (string.IsNullOrWhiteSpace(databasePasswordHash))
            {
                return false;
            }

            string enteredHash = Create(enteredPassword, legacyCodePage);

            byte[] enteredBytes = Encoding.ASCII.GetBytes(enteredHash);
            byte[] storedBytes = Encoding.ASCII.GetBytes(
                databasePasswordHash.Trim().ToLowerInvariant());

            try
            {
                return enteredBytes.Length == storedBytes.Length &&
                       CryptographicOperations.FixedTimeEquals(
                           enteredBytes,
                           storedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(enteredBytes);
                CryptographicOperations.ZeroMemory(storedBytes);
            }
        }
    }
}
