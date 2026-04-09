using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers.OData;

public sealed class TimelineEntriesController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public TimelineEntriesController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 1000)]
    public IQueryable<TimelineEntry> Get()
    {
        return _db.TimelineEntries;
    }
}
