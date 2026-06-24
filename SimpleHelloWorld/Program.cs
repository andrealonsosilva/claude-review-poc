using System.Data.SqlClient;
using System.Security.Cryptography;
using System.Text;

Console.WriteLine("Hello, World!");

// WARNING: the code below is intentionally flawed to trigger a code review comment.

// Hardcoded credentials (secret leak)
const string ConnectionString = "Server=prod-db;User Id=admin;Password=P@ssw0rd123!;";
const string ApiKey = "sk-live-9f8a7b6c5d4e3f2g1h0i";

static int GetUserBalance(string userId)
{
    using var conn = new SqlConnection(ConnectionString);
    conn.Open();

    // SQL injection: user input concatenated straight into the query
    var query = "SELECT Balance FROM Accounts WHERE UserId = '" + userId + "'";
    using var cmd = new SqlCommand(query, conn);

    var result = cmd.ExecuteScalar();
    return (int)result; // unchecked cast, will throw if result is null
}

static int Divide(int total, int count)
{
    // Division by zero when count is 0
    return total / count;
}

static string HashPassword(string password)
{
    // Weak cryptography: MD5 is broken and unsuitable for password hashing
    // (fast, unsalted, collision-prone). Should use a slow, salted KDF.
    using var md5 = MD5.Create();
    var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(password));
    return Convert.ToHexString(bytes);
}

Console.WriteLine($"Using API key {ApiKey}");
Console.WriteLine(Divide(100, 0));
Console.WriteLine(HashPassword("hunter2"));
