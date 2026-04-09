using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers.OData;

public sealed class WorldElementsController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public WorldElementsController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 200)]
    public IQueryable<WorldElement> Get()
    {
        return _db.WorldElements;
    }
}
