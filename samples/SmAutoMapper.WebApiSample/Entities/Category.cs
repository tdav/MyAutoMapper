namespace SmAutoMapper.WebApiSample.Entities;

public class Category
{
    public int Id { get; set; }
    public string? NameUz { get; set; }
    public string? NameLt { get; set; }
    public string? NameRu { get; set; }
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];
    public List<Product> Products { get; set; } = [];
}
