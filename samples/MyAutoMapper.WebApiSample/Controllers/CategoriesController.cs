using Microsoft.AspNetCore.Mvc;
using MyAutoMapper.WebApiSample.Data;
using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace MyAutoMapper.WebApiSample.Controllers;

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
    /// Returns categories as a tree with localized names.
    /// Uses parameterized ProjectTo for localization,
    /// then builds the hierarchy in memory.
    /// </summary>
    [HttpGet("tree")]
    public IActionResult GetTree([FromQuery] string lang = "ru")
    {
        // 1. ProjectTo: EF Core selects only Id, LocalizedName, ParentId
        //    with @__lang_0 SQL parameter for localization
        var flatCategories = _db.Categories
            .ProjectTo<Category, CategoryViewModel>(_projections,
                p => p.Set("lang", lang))
            .ToList();

        // We need ParentId to build the tree, but CategoryViewModel doesn't have it.
        // Load the Id→ParentId mapping separately.
        var parentMap = _db.Categories
            .Select(c => new { c.Id, c.ParentId })
            .ToDictionary(c => c.Id, c => c.ParentId);

        // 2. Build hierarchy in memory
        var lookup = flatCategories.ToDictionary(c => c.Id);
        var roots = new List<CategoryViewModel>();

        foreach (var cat in flatCategories)
        {
            if (parentMap.TryGetValue(cat.Id, out var parentId) && parentId.HasValue)
            {
                if (lookup.TryGetValue(parentId.Value, out var parent))
                {
                    parent.SubCategories.Add(cat);
                }
            }
            else
            {
                roots.Add(cat);
            }
        }

        return Ok(roots);
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
