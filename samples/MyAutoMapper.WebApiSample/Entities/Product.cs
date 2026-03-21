namespace MyAutoMapper.WebApiSample.Entities;

public class Product
{
    public int Id { get; set; }
    public string? NameUz { get; set; }
    public string? NameLt { get; set; }
    public string? NameRu { get; set; }
    public string? DescriptionUz { get; set; }
    public string? DescriptionLt { get; set; }
    public string? DescriptionRu { get; set; }
    public decimal Price { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
}
