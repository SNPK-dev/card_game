using System.Threading.Channels;
using Game.Application.Catalog;
using Game.Application.Players;
using Game.Shared.Catalog;

namespace Game.Api;

public sealed class PlayerGenerationJobWorker(
  PlayerAccountService accounts,
  ICatalogSnapshotStore catalogStore,
  IPlayerGenerationExecutor executor,
  TimeProvider clock,
  ILogger<PlayerGenerationJobWorker> logger) : BackgroundService, IPlayerGenerationJobQueue
{
  private readonly Channel<(string PlayerId, string JobId)> queue = Channel.CreateUnbounded<(string, string)>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
  public void Enqueue(string playerId, string jobId) => queue.Writer.TryWrite((playerId, jobId));

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
    {
      try { await ProcessAsync(item.PlayerId, item.JobId, stoppingToken); }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
      catch (Exception ex)
      {
        logger.LogError(ex,"Player generation job {JobId} failed unexpectedly.",item.JobId);
        var account=accounts.FindById(item.PlayerId);var job=account?.GenerationJobs.FirstOrDefault(x=>x.JobId==item.JobId);
        if(account is not null&&job is not null)SaveJob(account,job with{Status="failed",ProgressPercent=0,ErrorCode="GENERATION_INTERNAL_ERROR",UpdatedAtUtc=clock.GetUtcNow()});
      }
    }
  }

  private async Task ProcessAsync(string playerId, string jobId, CancellationToken stoppingToken)
  {
    var account = accounts.FindById(playerId); var job = account?.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
    if (account is null || job is null || job.Status == "cancelled") return;
    SaveJob(account, job with { Status = "running", ProgressPercent = 15, UpdatedAtUtc = clock.GetUtcNow() });
    var result = await executor.ExecuteAsync(new PlayerGenerationExecutionRequest(job.JobId, job.Prompt, job.ProviderMode, job.RequestedCount, job.Attempt, progress =>
    {
      var currentAccount = accounts.FindById(playerId);
      var currentJob = currentAccount?.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
      if (currentAccount is not null && currentJob is not null && currentJob.Status == "running" && !currentJob.CancelRequested)
        SaveJob(currentAccount, currentJob with { ProgressPercent = Math.Clamp(progress, 15, 65), UpdatedAtUtc = clock.GetUtcNow() });
    }), stoppingToken);
    account = accounts.FindById(playerId); job = account?.GenerationJobs.FirstOrDefault(x => x.JobId == jobId);
    if (account is null || job is null) return;
    if (job.CancelRequested) { SaveJob(account, job with { Status = "cancelled", ProgressPercent = 0, UpdatedAtUtc = clock.GetUtcNow() }); return; }
    if (!result.Succeeded) { SaveJob(account, job with { Status = "failed", ProgressPercent = 0, ErrorCode = result.ErrorCode, UpdatedAtUtc = clock.GetUtcNow() }); return; }
    SaveJob(account, job with { Status = "validating", ProgressPercent = 70, UpdatedAtUtc = clock.GetUtcNow() });
    var generatedCatalog = result.PublishedCatalog;
    if (generatedCatalog is null || generatedCatalog.Cards.Count < job.RequestedCount)
    {
      account = accounts.FindById(playerId)!; job = account.GenerationJobs.First(x => x.JobId == jobId);
      SaveJob(account, job with { Status = "failed", ProgressPercent = 0, ErrorCode = "GENERATION_CATALOG_NOT_READY", UpdatedAtUtc = clock.GetUtcNow() }); return;
    }
    var catalog = MergeWithLatestCatalog(catalogStore.Latest, generatedCatalog);
    var stored = catalogStore.Store(catalog);
    if (!stored.Succeeded)
    {
      account = accounts.FindById(playerId)!; job = account.GenerationJobs.First(x => x.JobId == jobId);
      SaveJob(account, job with { Status = "failed", ProgressPercent = 0, ErrorCode = "GENERATION_CATALOG_PUBLICATION_FAILED", UpdatedAtUtc = clock.GetUtcNow() }); return;
    }
    var now = clock.GetUtcNow();
    var acquired = generatedCatalog.Cards
      .Select(card => new PlayerCollectionEntry(card.CardId, card.CardVersionId, 2, false, now)).ToArray();
    account = accounts.FindById(playerId)!; job = account.GenerationJobs.First(x => x.JobId == jobId);
    var merged = account.Collection.Concat(acquired).GroupBy(x => x.CardVersionId, StringComparer.Ordinal).Select(g => g.First()).ToArray();
    SaveJob(account with { Collection = merged }, job with { Status = "completed", ProgressPercent = 100, Results = acquired, ErrorCode = null, UpdatedAtUtc = now });
  }

  private void SaveJob(PlayerAccount account, PlayerGenerationJob job) => accounts.Save(account with { GenerationJobs = account.GenerationJobs.Where(x => x.JobId != job.JobId).Append(job).ToArray() });

  private static CatalogSnapshot MergeWithLatestCatalog(CatalogSnapshot? latest, CatalogSnapshot generated)
  {
    if (latest is null || !string.Equals(latest.RulesetProfileId, generated.RulesetProfileId, StringComparison.Ordinal) ||
        !string.Equals(latest.RulesetProfileHash, generated.RulesetProfileHash, StringComparison.Ordinal)) return generated;

    var cards = latest.Cards.Concat(generated.Cards)
      .GroupBy(card => card.CardVersionId, StringComparer.Ordinal)
      .Select(group => group.Last())
      .Select(card => card with
      {
        CatalogSnapshotId = generated.SnapshotId,
        Definition = card.Definition with { CatalogSnapshotId = generated.SnapshotId }
      })
      .ToArray();
    return generated with
    {
      Cards = cards,
      CompatibleRulesetIds = (latest.CompatibleRulesetIds ?? []).Concat(generated.CompatibleRulesetIds ?? []).Distinct(StringComparer.Ordinal).ToArray(),
      CompatibleRulesetVersions = (latest.CompatibleRulesetVersions ?? []).Concat(generated.CompatibleRulesetVersions ?? []).Distinct(StringComparer.Ordinal).ToArray(),
      ProductionScaleCompatible = latest.ProductionScaleCompatible && generated.ProductionScaleCompatible
    };
  }
}
