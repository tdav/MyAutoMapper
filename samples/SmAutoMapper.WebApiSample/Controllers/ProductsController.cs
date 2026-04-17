using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmAutoMapper.WebApiSample.Data;
using SmAutoMapper.WebApiSample.ViewModels;
using SmAutoMapper.Extensions;

#pragma warning disable SMAM0002 // sample intentionally demonstrates legacy single-generic ProjectTo; migrate in 2.0

namespace SmAutoMapper.WebApiSample.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(AppDbContext db) : ControllerBase
{
    private readonly AppDbContext _db = db;

    /// <summary>
    /// GET /api/products?lang=ru
    /// Returns all products with localized name and description.
    /// EF Core generates SQL with @__lang_0 parameter (not inlined constant).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string lang = "ru")
    {
        var products = await _db.Products
            .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
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
            .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
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
            .ProjectTo<ProductViewModel>(p => p.Set("lang", lang))
            .ToListAsync();

        return Ok(products);
    }
}

#pragma warning restore SMAM0002
