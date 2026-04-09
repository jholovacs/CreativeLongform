using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers.OData;

public sealed class WorldElementLinksController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public WorldElementLinksController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 200)]
    public IQueryable<WorldElementLink> Get()
    {
        return _db.WorldElementLinks;
    }
}
