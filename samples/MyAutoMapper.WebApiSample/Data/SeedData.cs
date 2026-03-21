using MyAutoMapper.WebApiSample.Entities;

namespace MyAutoMapper.WebApiSample.Data;

public static class SeedData
{
    public static void Initialize(AppDbContext db)
    {
        db.Database.EnsureCreated();

        if (db.Categories.Any())
            return;

        // --- Categories (hierarchical) ---
        var electronics = new Category
        {
            NameUz = "Elektronika",
            NameLt = "Elektronika",
            NameRu = "Электроника"
        };
        var phones = new Category
        {
            NameUz = "Telefonlar",
            NameLt = "Telefonlar",
            NameRu = "Телефоны",
            Parent = electronics
        };
        var laptops = new Category
        {
            NameUz = "Noutbuklar",
            NameLt = "Noutbuklar",
            NameRu = "Ноутбуки",
            Parent = electronics
        };
        var clothing = new Category
        {
            NameUz = "Kiyim-kechak",
            NameLt = "Kiyim-kechak",
            NameRu = "Одежда"
        };
        var menClothing = new Category
        {
            NameUz = "Erkaklar kiyimi",
            NameLt = "Erkaklar kiyimi",
            NameRu = "Мужская одежда",
            Parent = clothing
        };

        db.Categories.AddRange(electronics, phones, laptops, clothing, menClothing);

        // --- Products ---
        db.Products.AddRange(
            new Product
            {
                NameUz = "iPhone 16 Pro",
                NameLt = "iPhone 16 Pro",
                NameRu = "iPhone 16 Pro",
                DescriptionUz = "Eng yangi Apple smartfoni",
                DescriptionLt = "Eng yangi Apple smartfoni",
                DescriptionRu = "Новейший смартфон Apple",
                Price = 12_990_000m,
                Category = phones
            },
            new Product
            {
                NameUz = "Samsung Galaxy S25",
                NameLt = "Samsung Galaxy S25",
                NameRu = "Samsung Galaxy S25",
                DescriptionUz = "Flagman Samsung smartfoni",
                DescriptionLt = "Flagman Samsung smartfoni",
                DescriptionRu = "Флагманский смартфон Samsung",
                Price = 10_490_000m,
                Category = phones
            },
            new Product
            {
                NameUz = "MacBook Air M4",
                NameLt = "MacBook Air M4",
                NameRu = "MacBook Air M4",
                DescriptionUz = "Yengil va kuchli noutbuk",
                DescriptionLt = "Yengil va kuchli noutbuk",
                DescriptionRu = "Легкий и мощный ноутбук",
                Price = 18_500_000m,
                Category = laptops
            },
            new Product
            {
                NameUz = "Klassik ko'ylak",
                NameLt = "Klassik ko'ylak",
                NameRu = "Классическая рубашка",
                DescriptionUz = "100% paxta, oq rang",
                DescriptionLt = "100% paxta, oq rang",
                DescriptionRu = "100% хлопок, белая",
                Price = 290_000m,
                Category = menClothing
            }
        ); 

        db.SaveChanges();
    }
}
