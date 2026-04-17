namespace SmAutoMapper.WebApiSample.ViewModels;

public class ProductViewModel
{
    public int Id { get; set; }
    public string? LocalizedName { get; set; }
    public string? LocalizedDescription { get; set; }
    public decimal Price { get; set; }
}
