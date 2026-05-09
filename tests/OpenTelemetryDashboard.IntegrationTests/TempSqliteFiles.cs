namespace OpenTelemetryDashboard.IntegrationTests;

/// <summary>
/// Test-fixture helper: deletes a SQLite database file along with the WAL
/// sidecar files (<c>-wal</c>, <c>-shm</c>) that <c>journal_mode=WAL</c>
/// produces. Best-effort — slow shutdown can leave the file locked, in which
/// case we leave the temp file behind.
/// </summary>
internal static class TempSqliteFiles
{
    public static void TryDelete(string databasePath)
    {
        foreach (var path in new[] { databasePath, databasePath + "-wal", databasePath + "-shm" })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // File still locked on slow shutdown; tolerable in temp.
            }
        }
    }
}
