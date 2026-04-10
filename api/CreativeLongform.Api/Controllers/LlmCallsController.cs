using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers;

public sealed class LlmCallsController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public LlmCallsController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 100)]
    public IQueryable<LlmCall> Get()
    {
        return _db.LlmCalls;
    }
}
