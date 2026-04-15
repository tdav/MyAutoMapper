namespace MyAutoMapper.WebApiSample.ViewModels;

public class CategoryViewModel
{
    public int Id { get; set; }
    public string? LocalizedName { get; set; }
    public List<CategoryViewModel> Children { get; set; } = [];
}
