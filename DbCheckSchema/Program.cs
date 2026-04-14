using System;
using Microsoft.Data.SqlClient;

class FixDatabase {
    static void Main() {
        var cs = "Server=DESKTOP-KFA0H13;Database=IronDeveloper;Integrated Security=True;Encrypt=True;TrustServerCertificate=True;";
        using var conn = new SqlConnection(cs);
        conn.Open();
        string[] tables = { "Projects", "ChatMessages", "ProjectSummaries", "ProjectDecisions", "ProjectTickets", "ProjectFiles" };
        foreach (var table in tables) {
            try {
                using var cmd = new SqlCommand($"ALTER TABLE dbo.{table} ADD TenantId INT NOT NULL DEFAULT 1", conn);
                cmd.ExecuteNonQuery();
                Console.WriteLine($"Added TenantId to {table}");
            } catch (SqlException ex) when (ex.Number == 2705) { // Column already exists
                Console.WriteLine($"TenantId already exists in {table}");
            } catch (Exception ex) {
                Console.WriteLine($"Failed to add to {table}: {ex.Message}");
            }
        }
    }
}
