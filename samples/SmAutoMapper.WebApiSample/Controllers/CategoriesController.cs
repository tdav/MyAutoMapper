using Microsoft.AspNetCore.Mvc;
using SmAutoMapper.WebApiSample.Data;
using SmAutoMapper.WebApiSample.Entities;
using SmAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace SmAutoMapper.WebApiSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IProjectionProvider _projections;

    public CategoriesController(AppDbContext db, IProjectionProvider projections)
    {
        _db = db;
        _projections = projections;
    }

    /// <summary>
    /// GET /api/categories/tree?lang=ru
    /// Returns categories as a tree. Hierarchy is projected recursively
    /// via <c>.MaxDepth(5)</c> on <see cref="CategoryViewModel.Children"/>.
    ///
    /// Note: the <c>lang</c> parameter is applied to the root level only.
    /// Nested children fall back to <c>NameRu</c> because parameter holders
    /// are not shared across recursive levels yet (tracked TODO).
    /// </summary>
    [HttpGet("tree")]
    public IActionResult GetTree([FromQuery] string lang = "ru")
    {
        var tree = _db.Categories
            .Where(c => c.ParentId == null)
            .ProjectTo<CategoryViewModel>(p => p.Set("lang", lang))
            .ToList();

        return Ok(tree);
    }

    /// <summary>
    /// GET /api/categories/flat?lang=ru
    /// Returns categories as a flat list with localized names.
    /// Pure ProjectTo — single SQL query with parameterized localization.
    /// </summary>
    [HttpGet("flat")]
    public IActionResult GetFlat([FromQuery] string lang = "ru")
    {
        var categories = _db.Categories
            .ProjectTo<Category, CategoryViewModel>(_projections,
                p => p.Set("lang", lang))
            .ToList();

        return Ok(categories);
    }


}
