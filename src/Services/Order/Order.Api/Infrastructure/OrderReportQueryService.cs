using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Order.Api.Infrastructure;

public sealed record DailySalesReportRow(
    string Day,
    int OrderCount,
    decimal TotalAmount,
    decimal RunningTotal,
    long RankByAmount);

/// <summary>
/// T-SQL CTE + Window Function (<c>SUM() OVER</c>, <c>DENSE_RANK() OVER</c>) kullanan
/// <c>dbo.sp_GunlukSatisRaporu</c> saklı yordamını Dapper ile çalıştırır.
/// </summary>
public sealed class OrderReportQueryService(IConfiguration configuration)
{
    private readonly string _connStr = configuration.GetConnectionString("OrderDb")
        ?? "Server=(localdb)\\MSSQLLocalDB;Database=ETicaret_OrderDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public async Task<IReadOnlyList<DailySalesReportRow>> GetDailySalesAsync(int days, CancellationToken ct)
    {
        await using var conn = new SqlConnection(_connStr);
        var rows = await conn.QueryAsync<DailySalesReportRow>(
            new CommandDefinition(
                "dbo.sp_GunlukSatisRaporu",
                new { GunSayisi = Math.Clamp(days, 1, 365) },
                commandType: CommandType.StoredProcedure,
                cancellationToken: ct));

        return rows.ToList();
    }

    public static async Task EnsureStoredProcedureAndSeedAsync(OrderDbContext db)
    {
        const string createSpSql = """
            CREATE OR ALTER PROCEDURE dbo.sp_GunlukSatisRaporu
                @GunSayisi INT = 30
            AS
            BEGIN
                SET NOCOUNT ON;

                DECLARE @BaslangicTarihi DATETIME2 = DATEADD(DAY, -@GunSayisi, SYSUTCDATETIME());

                ;WITH GunlukOzet AS (
                    SELECT
                        CONVERT( char(10), o.CreatedAt, 23 ) AS Day,
                        COUNT(1)                             AS OrderCount,
                        SUM(o.TotalAmount)                   AS TotalAmount
                    FROM dbo.Orders AS o
                    WHERE o.Status = N'Confirmed'
                      AND o.CreatedAt >= @BaslangicTarihi
                    GROUP BY CONVERT( char(10), o.CreatedAt, 23 )
                )
                SELECT
                    g.Day,
                    g.OrderCount,
                    g.TotalAmount,
                    SUM(g.TotalAmount) OVER (ORDER BY g.Day ROWS UNBOUNDED PRECEDING) AS RunningTotal,
                    DENSE_RANK()       OVER (ORDER BY g.TotalAmount DESC)             AS RankByAmount
                FROM GunlukOzet AS g
                ORDER BY g.Day DESC;
            END
            """;

        await db.Database.ExecuteSqlRawAsync(createSpSql);
    }
}

