using CreativeLongform.Application.Abstractions;
using CreativeLongform.Domain.Entities;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;

namespace CreativeLongform.Api.Controllers;

public sealed class BooksController : ODataController
{
    private readonly ICreativeLongformDbContext _db;

    public BooksController(ICreativeLongformDbContext db)
    {
        _db = db;
    }

    [EnableQuery(PageSize = 100)]
    public IQueryable<Book> Get()
    {
        return _db.Books;
    }
}
