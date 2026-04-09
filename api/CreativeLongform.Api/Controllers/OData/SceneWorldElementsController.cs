using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers.OData;

public sealed class SceneWorldElementsController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public SceneWorldElementsController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 200)]
    public IQueryable<SceneWorldElement> Get()
    {
        return _db.SceneWorldElements;
    }
}
