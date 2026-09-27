using Accession.Core.Model;
using Accession.Core.Runtime;
using Accession.Data;
using Accession.Data.Audit;
using Accession.Data.Locking;
using Accession.Data.Migrations;
using Accession.Data.Repositories;
using Accession.Data.Sessions;
using Accession.Tests.TestSupport;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Accession.Tests.Data;

public sealed class InventoryOpenServiceTests : IDisposable
{
    private readonly TestInventory _inventory = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 9, 0, 0, TimeSpan.Zero));
    private readonly List<InventorySession> _sessions = [];

    public void Dispose()
    {
        foreach (var session in _sessions)
        {
            session.Close();
        }

        _inventory.Dispose();
    }

    private sealed record User(string UserName, string MachineName) : IUserContext;

    private sealed class App : IAppInfo
    {
        public string Version => "0.2.0";
    }

    private sealed class Interaction : IOpenInteraction
    {
        public bool UpgradeAnswer { get; set; } = true;
        public Queue<LockConflictChoice> LockAnswers { get; } = new();
        public Queue<Func<string, RootUnreachableResolution>> RootAnswers { get; } = new();
        public int UpgradeQuestions { get; private set; }
        public List<LockConflict> LockQuestions { get; } = [];
        public int RootQuestions { get; private set; }

        public bool ConfirmUpgrade(int fromVersion, int toVersion)
        {
            UpgradeQuestions++;
            return UpgradeAnswer;
        }

        public LockConflictChoice ResolveLockConflict(LockConflict conflict)
        {
            LockQuestions.Add(conflict);
            return LockAnswers.Count > 0 ? LockAnswers.Dequeue() : LockConflictChoice.Cancel;
        }

        public RootUnreachableResolution ResolveRootUnreachable(string rootPath)
        {
            RootQuestions++;
            return RootAnswers.Count > 0 ? RootAnswers.Dequeue()(rootPath) : new RootUnreachableResolution(RootUnreachableChoice.Cancel);
        }
    }

    private InventoryOpenService Service(string user = @"CORP\jdoe", string machine = "WS-114", IProcessProbe? processes = null) =>
        new(new InventorySessionFactory(new User(user, machine), _time, NullLoggerFactory.Instance, processes),
            new App(), new RootPathService(), NullLogger<InventoryOpenService>.Instance);

    private InventorySession? Open(Interaction interaction, string user = @"CORP\jdoe", string machine = "WS-114", IProcessProbe? processes = null)
    {
        var session = Service(user, machine, processes).Open(_inventory.DbPath, interaction);
        if (session is not null)
        {
            _sessions.Add(session);
        }

        return session;
    }

    private IReadOnlyList<AuditAction> AuditActions() =>
        new AuditService(_inventory.Database, new User("x", "y"), _time).Query(new AuditQuery()).Select(a => a.Action).Reverse().ToList();

    [Fact]
    public void Opens_current_inventory_with_lock_and_updates_last_opened()
    {
        var interaction = new Interaction();
        var session = Open(interaction)!;

        Assert.False(session.IsReadOnly);
        Assert.True(session.IsRootAvailable);
        Assert.True(session.LockService!.IsHeld);
        Assert.Equal(_time.GetUtcNow(), session.Config.LastOpenedAtUtc);
        Assert.Equal(@"CORP\jdoe", session.Config.LastOpenedBy);
        Assert.Equal(_inventory.DbPath, session.Config.LastDbPath);
        Assert.Equal([AuditAction.LockAcquired, AuditAction.InventoryOpened], AuditActions());
        Assert.Equal(0, interaction.UpgradeQuestions);
        Assert.Empty(interaction.LockQuestions);
    }

    private sealed class Processes(bool running) : IProcessProbe
    {
        public bool IsAccessionRunning(int processId) => running;
    }

    [Fact]
    public void Own_lock_left_by_a_session_that_is_gone_is_recovered_and_opens_normally()
    {
        Open(new Interaction())!.LockService!.Dispose(); // same user and computer; the app "crashed" without releasing
        var interaction = new Interaction();

        var session = Open(interaction, processes: new Processes(running: false))!;

        Assert.False(session.IsReadOnly);
        Assert.True(session.LockService!.IsHeld);
        Assert.Empty(interaction.LockQuestions);
        Assert.Contains("not closed properly", session.OpenNotice, StringComparison.Ordinal);
        Assert.Contains(AuditAction.LockRecovered, AuditActions());
    }

    [Fact]
    public void Own_lock_held_by_another_running_window_asks_for_read_only()
    {
        Open(new Interaction());
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.OpenReadOnly);

        var session = Open(interaction, processes: new Processes(running: true))!;

        Assert.True(session.IsReadOnly);
        Assert.True(Assert.Single(interaction.LockQuestions).IsOtherWindowHere);
        Assert.Empty(session.OpenNotice);
        Assert.Equal(interaction.LockQuestions[0].Holder.SessionGuid, session.ReadLockHolder()?.SessionGuid); // for the read-only chip
    }

    [Fact]
    public void Lock_of_the_same_user_on_another_computer_is_someone_else()
    {
        Open(new Interaction(), machine: "LAPTOP-7")!.LockService!.Dispose();
        var interaction = new Interaction();

        Assert.Null(Open(interaction, processes: new Processes(running: false)));

        var question = Assert.Single(interaction.LockQuestions);
        Assert.False(question.IsOtherWindowHere);
        Assert.Equal("LAPTOP-7", question.Holder.MachineName);
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        SetSchemaVersion(99);

        Assert.Throws<SchemaTooNewException>(() => Open(new Interaction()));
    }

    [Fact]
    public void Non_inventory_is_refused()
    {
        var path = _inventory.Temp.Combine("notes.sqlite");
        File.WriteAllText(path, new string('x', 200));

        Assert.Throws<NotAnInventoryException>(() => Service().Open(path, new Interaction()));
    }

    [Fact]
    public void Older_schema_upgrades_after_confirmation_with_backup_and_audit()
    {
        SetSchemaVersion(0);

        var session = Open(new Interaction { UpgradeAnswer = true })!;

        Assert.Equal(1, session.Config.SchemaVersion);
        Assert.Single(Directory.GetFiles(_inventory.Temp.Path, "*.v0.*.bak"));
        Assert.Contains(AuditAction.SchemaUpgraded, AuditActions());
    }

    [Fact]
    public void Declining_upgrade_opens_nothing()
    {
        SetSchemaVersion(0);

        Assert.Null(Open(new Interaction { UpgradeAnswer = false }));
        Assert.Empty(Directory.GetFiles(_inventory.Temp.Path, "*.bak"));
        Assert.DoesNotContain(AuditAction.LockAcquired, AuditActions());
    }

    [Fact]
    public void Lock_conflict_can_be_cancelled()
    {
        Open(new Interaction(), @"CORP\asmith", "WS-203");
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.Cancel);

        Assert.Null(Open(interaction));
        var question = Assert.Single(interaction.LockQuestions);
        Assert.Equal(@"CORP\asmith", question.Holder.UserName);
        Assert.Equal("WS-203", question.Holder.MachineName);
        Assert.False(question.IsStale);
    }

    [Fact]
    public void Lock_conflict_can_open_read_only()
    {
        var holder = Open(new Interaction(), @"CORP\asmith", "WS-203")!;
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.OpenReadOnly);

        var session = Open(interaction)!;

        Assert.True(session.IsReadOnly);
        Assert.Null(session.LockService);
        Assert.True(session.Database.IsReadOnly);
        Assert.Equal(holder.LockService!.SessionGuid, holder.LockService.ReadHolder()!.SessionGuid);
        Assert.Contains(AuditAction.InventoryOpenedReadOnly, AuditActions());
        Assert.Throws<InvalidOperationException>(session.EnsureWritable);
    }

    [Fact]
    public void Read_only_is_not_possible_when_an_upgrade_is_needed()
    {
        Open(new Interaction(), @"CORP\asmith", "WS-203");
        SetSchemaVersion(0);
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.OpenReadOnly);

        var ex = Assert.Throws<InventoryInUseException>(() => Open(interaction));
        Assert.Equal(@"CORP\asmith", ex.Holder.UserName);
    }

    [Fact]
    public void Stale_lock_can_be_taken_over()
    {
        Open(new Interaction(), @"CORP\asmith", "WS-203")!.LockService!.Dispose(); // stop heartbeat: simulates a crash
        _time.Advance(TimeSpan.FromMinutes(11));
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.TakeOver);

        var session = Open(interaction)!;

        Assert.False(session.IsReadOnly);
        Assert.True(Assert.Single(interaction.LockQuestions).IsStale);
        Assert.Contains(AuditAction.LockForced, AuditActions());
    }

    [Fact]
    public void Take_over_of_fresh_lock_is_ignored_and_asks_again()
    {
        Open(new Interaction(), @"CORP\asmith", "WS-203");
        var interaction = new Interaction();
        interaction.LockAnswers.Enqueue(LockConflictChoice.TakeOver);
        interaction.LockAnswers.Enqueue(LockConflictChoice.Cancel);

        Assert.Null(Open(interaction));
        Assert.Equal(2, interaction.LockQuestions.Count);
        Assert.DoesNotContain(AuditAction.LockForced, AuditActions());
    }

    [Fact]
    public void Unreachable_root_retry_succeeds_when_folder_comes_back()
    {
        Directory.Delete(_inventory.RootPath);
        var interaction = new Interaction();
        interaction.RootAnswers.Enqueue(root =>
        {
            Directory.CreateDirectory(root);
            return new RootUnreachableResolution(RootUnreachableChoice.Retry);
        });

        var session = Open(interaction)!;

        Assert.True(session.IsRootAvailable);
        Assert.Equal(1, interaction.RootQuestions);
    }

    [Fact]
    public void Unreachable_root_can_continue_offline()
    {
        Directory.Delete(_inventory.RootPath);
        var interaction = new Interaction();
        interaction.RootAnswers.Enqueue(_ => new RootUnreachableResolution(RootUnreachableChoice.ContinueOffline));

        var session = Open(interaction)!;

        Assert.False(session.IsRootAvailable);
        Assert.False(session.IsReadOnly);
    }

    [Fact]
    public void Unreachable_root_cancel_closes_and_releases_lock()
    {
        Directory.Delete(_inventory.RootPath);

        Assert.Null(Open(new Interaction()));

        using var scope = _inventory.Database.Open();
        Assert.Equal(0L, scope.Connection.ExecuteScalar<long>("SELECT IsLocked FROM InventoryLock"));
        Assert.Equal(AuditAction.LockReleased, AuditActions()[^1]);
    }

    [Fact]
    public void Unreachable_root_can_be_changed()
    {
        var newRoot = _inventory.Temp.Combine("Moved", "Media");
        Directory.CreateDirectory(newRoot);
        Directory.Delete(_inventory.RootPath);
        var interaction = new Interaction();
        interaction.RootAnswers.Enqueue(_ => new RootUnreachableResolution(RootUnreachableChoice.ChangeRootPath, newRoot));

        var session = Open(interaction)!;

        Assert.Equal(newRoot, session.Config.RootPath);
        Assert.True(session.IsRootAvailable);
        Assert.Contains(AuditAction.RootPathChanged, AuditActions());
    }

    [Fact]
    public void Root_path_preview_lists_media_not_found()
    {
        var session = Open(new Interaction())!;
        using (var scope = session.Database.Open())
        {
            var media = new MediaRepository(scope);
            media.Insert("M1", @"\M1\", _time.GetUtcNow(), "u");
            media.Insert("M2", @"\M2\", _time.GetUtcNow(), "u");
        }

        var newRoot = _inventory.Temp.Combine("New");
        Directory.CreateDirectory(Path.Combine(newRoot, "m1")); // case-insensitive match on Windows only
        Directory.CreateDirectory(Path.Combine(newRoot, "M1"));

        var service = new RootPathService();
        var preview = service.Preview(session, newRoot + Path.DirectorySeparatorChar);
        Assert.True(preview.Exists);
        Assert.Equal(newRoot, preview.NewRootPath);
        Assert.Equal(["M2"], preview.MediaNotFound);

        Assert.False(service.Preview(session, _inventory.Temp.Combine("nope")).Exists);
        Assert.Throws<DirectoryNotFoundException>(() => service.Apply(session, _inventory.Temp.Combine("nope")));

        service.Apply(session, newRoot);
        Assert.Equal(newRoot, session.Config.RootPath);
        var audit = session.Audit.Query(new AuditQuery { Actions = [AuditAction.RootPathChanged] }).Single();
        Assert.Contains("\"mediaNotFound\":[\"M2\"]", audit.Details);
    }

    private void SetSchemaVersion(int version)
    {
        using var connection = SqliteConnectionFactory.Open(_inventory.DbPath);
        connection.Execute("UPDATE InventoryConfig SET SchemaVersion = @version", new { version });
    }
}
