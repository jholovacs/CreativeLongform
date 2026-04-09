using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers;

public sealed class GenerationRunsController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public GenerationRunsController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 100)]
    public IQueryable<GenerationRun> Get()
    {
        return _db.GenerationRuns;
    }
}
