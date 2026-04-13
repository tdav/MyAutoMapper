using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyAutoMapper.WebApiSample.Data;
using MyAutoMapper.WebApiSample.Entities;
using MyAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Extensions;
using SmAutoMapper.Runtime;

namespace MyAutoMapper.WebApiSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IProjectionProvider _projections;

    public ProductsController(AppDbContext db, IProjectionProvider projections)
    {
        _db = db;
        _projections = projections;
    }

    /// <summary>
    /// GET /api/products?lang=ru
    /// Returns all products with localized name and description.
    /// EF Core generates SQL with @__lang_0 parameter (not inlined constant).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string lang = "ru")
    {
        var products = await _db.Products
            .ProjectTo<Product, ProductViewModel>(_projections,
                p => p.Set("lang", lang))
            .ToListAsync();

        return Ok(products);
    }

    /// <summary>
    /// GET /api/products/{id}?lang=ru
    /// Returns a single product with localized fields.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, [FromQuery] string lang = "ru")
    {
        var product = await _db.Products
            .Where(p => p.Id == id)
            .ProjectTo<Product, ProductViewModel>(_projections,
                p => p.Set("lang", lang))
            .FirstOrDefaultAsync();

        if (product is null)
            return NotFound();

        return Ok(product);
    }

    /// <summary>
    /// GET /api/products/by-category/{categoryId}?lang=ru
    /// Returns products filtered by category.
    /// </summary>
    [HttpGet("by-category/{categoryId:int}")]
    public async Task<IActionResult> GetByCategory(int categoryId, [FromQuery] string lang = "ru")
    {
        var products = await _db.Products
            .Where(p => p.CategoryId == categoryId)
            .ProjectTo<Product, ProductViewModel>(_projections,
                p => p.Set("lang", lang))
            .ToListAsync();

        return Ok(products);
    }
}
