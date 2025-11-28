// SPRINT 1: Discover MexcContract properties using reflection
using Mexc.Net.Objects.Models.Futures;
using System;
using System.Linq;
using System.Reflection;

class TestContractProperties
{
    static void Main()
    {
        Console.WriteLine("=== MexcContract Properties ===\n");

        var contractType = typeof(MexcContract);
        var properties = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        Console.WriteLine($"Found {properties.Length} properties:\n");

        foreach (var prop in properties.OrderBy(p => p.Name))
        {
            Console.WriteLine($"  {prop.PropertyType.Name} {prop.Name} {{ get; set; }}");
        }
    }
}
