namespace MyAutoMapper.Benchmarks;

// === Simple flat DTO (3 properties) ===
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

// === Complex flat DTO (10 scalar properties) ===
public class ComplexSource
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Salary { get; set; }
    public string Department { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
}

public class ComplexDest
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public int Age { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Salary { get; set; }
    public string Department { get; set; } = "";
    public string PhoneNumber { get; set; } = "";
}

// === Flattening DTO (nested → flat) ===
public class FlattenSource
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public FlattenAddress Address { get; set; } = new();
}

public class FlattenAddress
{
    public string Street { get; set; } = "";
    public string City { get; set; } = "";
    public string ZipCode { get; set; } = "";
    public string Country { get; set; } = "";
}

public class FlattenDest
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    public string AddressStreet { get; set; } = "";
    public string AddressCity { get; set; } = "";
    public string AddressZipCode { get; set; } = "";
    public string AddressCountry { get; set; } = "";
}
