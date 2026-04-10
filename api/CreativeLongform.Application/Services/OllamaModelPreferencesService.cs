using CreativeLongform.Application.Abstractions;
using CreativeLongform.Application.Options;
using CreativeLongform.Domain.Entities;
using CreativeLongform.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CreativeLongform.Application.Services;

public sealed class OllamaModelPreferencesService : IOllamaModelPreferencesService
{
    private readonly ICreativeLongformDbContext _db;
    private readonly IOptions<OllamaOptions> _options;

    public OllamaModelPreferencesService(ICreativeLongformDbContext db, IOptions<OllamaOptions> options)
    {
        _db = db;
        _options = options;
    }

    public async Task<OllamaModelAssignmentsDto> GetAssignmentsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        return ToDto(row);
    }

    public async Task<OllamaModelAssignmentsDto> UpdateAssignmentsAsync(
        OllamaModelAssignmentsPatch patch,
        string source,
        CancellationToken cancellationToken = default)
    {
        var before = await GetAssignmentsAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);

        if (patch.ClearWriter == true)
            row.WriterModel = null;
        else if (patch.WriterModel is { } w)
        {
            var t = w.Trim();
            row.WriterModel = string.IsNullOrEmpty(t) ? null : t;
        }

        if (patch.ClearCritic == true)
            row.CriticModel = null;
        else if (patch.CriticModel is { } c)
        {
            var t = c.Trim();
            row.CriticModel = string.IsNullOrEmpty(t) ? null : t;
        }

        if (patch.ClearAgent == true)
            row.AgentModel = null;
        else if (patch.AgentModel is { } a)
        {
            var t = a.Trim();
            row.AgentModel = string.IsNullOrEmpty(t) ? null : t;
        }

        if (patch.ClearWorldBuilding == true)
            row.WorldBuildingModel = null;
        else if (patch.WorldBuildingModel is { } wb)
        {
            var t = wb.Trim();
            row.WorldBuildingModel = string.IsNullOrEmpty(t) ? null : t;
        }

        if (patch.ClearPreState == true)
            row.PreStateModel = null;
        else if (patch.PreStateModel is { } ps)
        {
            var t = ps.Trim();
            row.PreStateModel = string.IsNullOrEmpty(t) ? null : t;
        }

        if (patch.ClearPostState == true)
            row.PostStateModel = null;
        else if (patch.PostStateModel is { } pst)
        {
            var t = pst.Trim();
            row.PostStateModel = string.IsNullOrEmpty(t) ? null : t;
        }

        row.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        var after = await GetAssignmentsAsync(cancellationToken);
        var src = string.IsNullOrWhiteSpace(source) ? "api" : source.Trim();

        void Log(OllamaModelRole role, string prev, string next)
        {
            if (string.Equals(prev, next, StringComparison.Ordinal))
                return;
            _db.OllamaModelChangeLogs.Add(new OllamaModelChangeLog
            {
                Id = Guid.NewGuid(),
                OccurredAt = DateTimeOffset.UtcNow,
                Role = role,
                PreviousModel = prev,
                NewModel = next,
                Source = src
            });
        }

        Log(OllamaModelRole.Writer, before.WriterModel, after.WriterModel);
        Log(OllamaModelRole.Critic, before.CriticModel, after.CriticModel);
        Log(OllamaModelRole.Agent, before.AgentModel, after.AgentModel);
        Log(OllamaModelRole.WorldBuilding, before.WorldBuildingModel, after.WorldBuildingModel);
        Log(OllamaModelRole.PreState, before.PreStateModel, after.PreStateModel);
        Log(OllamaModelRole.PostState, before.PostStateModel, after.PostStateModel);

        await _db.SaveChangesAsync(cancellationToken);
        return after;
    }

    public async Task<IReadOnlyList<OllamaModelChangeLogDto>> GetChangeLogAsync(int take,
        CancellationToken cancellationToken = default)
    {
        take = Math.Clamp(take, 1, 500);
        return await _db.OllamaModelChangeLogs.AsNoTracking()
            .OrderByDescending(x => x.OccurredAt)
            .Take(take)
            .Select(x => new OllamaModelChangeLogDto
            {
                Id = x.Id,
                OccurredAt = x.OccurredAt,
                Role = x.Role,
                PreviousModel = x.PreviousModel,
                NewModel = x.NewModel,
                Source = x.Source
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<string> GetWriterModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        return EffectiveWriter(row);
    }

    public async Task<string> GetCriticModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        return EffectiveCritic(row);
    }

    public async Task<string> GetAgentModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        var o = _options.Value;
        return EffectiveAgent(row, o);
    }

    public async Task<string> GetWorldBuildingModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        var o = _options.Value;
        return EffectiveWorldBuilding(row, o);
    }

    public async Task<string> GetPreStateModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        var o = _options.Value;
        return EffectivePreState(row, o);
    }

    public async Task<string> GetPostStateModelAsync(CancellationToken cancellationToken = default)
    {
        await EnsureSingletonAsync(cancellationToken);
        var row = await _db.OllamaModelPreferences.AsNoTracking()
            .FirstAsync(x => x.Id == OllamaModelPreferences.SingletonId, cancellationToken);
        var o = _options.Value;
        return EffectivePostState(row, o);
    }

    private OllamaModelAssignmentsDto ToDto(OllamaModelPreferences row)
    {
        var o = _options.Value;
        var writer = EffectiveWriter(row);
        var critic = EffectiveCritic(row);
        var agent = EffectiveAgent(row, o);
        var wb = EffectiveWorldBuilding(row, o);
        var pre = EffectivePreState(row, o);
        var post = EffectivePostState(row, o);
        var overridden = new List<OllamaModelRole>();
        if (!string.IsNullOrWhiteSpace(row.WriterModel)) overridden.Add(OllamaModelRole.Writer);
        if (!string.IsNullOrWhiteSpace(row.CriticModel)) overridden.Add(OllamaModelRole.Critic);
        if (!string.IsNullOrWhiteSpace(row.AgentModel)) overridden.Add(OllamaModelRole.Agent);
        if (!string.IsNullOrWhiteSpace(row.WorldBuildingModel)) overridden.Add(OllamaModelRole.WorldBuilding);
        if (!string.IsNullOrWhiteSpace(row.PreStateModel)) overridden.Add(OllamaModelRole.PreState);
        if (!string.IsNullOrWhiteSpace(row.PostStateModel)) overridden.Add(OllamaModelRole.PostState);
        return new OllamaModelAssignmentsDto
        {
            WriterModel = writer,
            CriticModel = critic,
            AgentModel = agent,
            WorldBuildingModel = wb,
            PreStateModel = pre,
            PostStateModel = post,
            DbOverriddenRoles = overridden.ToArray()
        };
    }

    private string EffectiveWriter(OllamaModelPreferences row) =>
        !string.IsNullOrWhiteSpace(row.WriterModel) ? row.WriterModel.Trim() : _options.Value.WriterModel.Trim();

    private string EffectiveCritic(OllamaModelPreferences row) =>
        !string.IsNullOrWhiteSpace(row.CriticModel) ? row.CriticModel.Trim() : _options.Value.CriticModel.Trim();

    private string EffectiveAgent(OllamaModelPreferences row, OllamaOptions o)
    {
        if (!string.IsNullOrWhiteSpace(row.AgentModel))
            return row.AgentModel.Trim();
        if (!string.IsNullOrWhiteSpace(o.AgentModel))
            return o.AgentModel.Trim();
        return EffectiveWriter(row);
    }

    private string EffectiveWorldBuilding(OllamaModelPreferences row, OllamaOptions o)
    {
        if (!string.IsNullOrWhiteSpace(row.WorldBuildingModel))
            return row.WorldBuildingModel.Trim();
        if (!string.IsNullOrWhiteSpace(o.WorldBuildingModel))
            return o.WorldBuildingModel.Trim();
        return EffectiveWriter(row);
    }

    private string EffectivePreState(OllamaModelPreferences row, OllamaOptions o)
    {
        if (!string.IsNullOrWhiteSpace(row.PreStateModel))
            return row.PreStateModel.Trim();
        if (!string.IsNullOrWhiteSpace(o.PreStateModel))
            return o.PreStateModel.Trim();
        return EffectiveWriter(row);
    }

    private string EffectivePostState(OllamaModelPreferences row, OllamaOptions o)
    {
        if (!string.IsNullOrWhiteSpace(row.PostStateModel))
            return row.PostStateModel.Trim();
        if (!string.IsNullOrWhiteSpace(o.PostStateModel))
            return o.PostStateModel.Trim();
        return EffectiveWriter(row);
    }

    private async Task EnsureSingletonAsync(CancellationToken cancellationToken)
    {
        if (await _db.OllamaModelPreferences.AnyAsync(x => x.Id == OllamaModelPreferences.SingletonId,
                cancellationToken))
            return;
        _db.OllamaModelPreferences.Add(new OllamaModelPreferences
        {
            Id = OllamaModelPreferences.SingletonId,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
