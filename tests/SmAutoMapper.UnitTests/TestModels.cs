namespace SmAutoMapper.UnitTests;

public class SimpleSource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class SimpleDest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

public class SourceWithNested
{
    public int Id { get; set; }
    public Address Address { get; set; } = new();
}

public class Address
{
    public string City { get; set; } = "";
    public string Street { get; set; } = "";
}

public class FlatDest
{
    public int Id { get; set; }
    public string AddressCity { get; set; } = "";
    public string AddressStreet { get; set; } = "";
}

public class LocalizedSource
{
    public int Id { get; set; }
    public string NameEn { get; set; } = "";
    public string NameFr { get; set; } = "";
    public string NameDefault { get; set; } = "";
}

public class LocalizedDest
{
    public int Id { get; set; }
    public string LocalizedName { get; set; } = "";
}

public class PartialDest
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Extra { get; set; } = "default";
}
