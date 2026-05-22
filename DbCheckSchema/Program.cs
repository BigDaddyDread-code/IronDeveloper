using System;
using System.Linq;
using System.Reflection;

class InspectAssembly {
    static void Main() {
        try {
            var assembly = Assembly.Load("Weaviate.Client");
            var t = assembly.GetType("Weaviate.Client.Models.Vectors");
            if (t != null) {
                Console.WriteLine($"Type: {t.FullName}");
                Console.WriteLine("Properties:");
                foreach (var prop in t.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                    Console.WriteLine($"  - {prop.PropertyType} {prop.Name} (get: {prop.CanRead}, set: {prop.CanWrite})");
                }
                
                Console.WriteLine("Methods:");
                foreach (var m in t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                    var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
                    Console.WriteLine($"  - {m.ReturnType} {m.Name}({parameters})");
                }

                // Also check Weaviate.Client.Models.Vector if it exists
                var vecType = assembly.GetType("Weaviate.Client.Models.Vector");
                if (vecType != null) {
                    Console.WriteLine($"\nType: {vecType.FullName}");
                    Console.WriteLine("Properties:");
                    foreach (var prop in vecType.GetProperties(BindingFlags.Public | BindingFlags.Instance)) {
                        Console.WriteLine($"  - {prop.PropertyType} {prop.Name} (get: {prop.CanRead}, set: {prop.CanWrite})");
                    }
                    Console.WriteLine("Methods:");
                    foreach (var m in vecType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)) {
                        var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
                        Console.WriteLine($"  - {m.ReturnType} {m.Name}({parameters})");
                    }
                    Console.WriteLine("Implicit/explicit conversion operators:");
                    foreach (var m in vecType.GetMethods(BindingFlags.Public | BindingFlags.Static)) {
                        var parameters = string.Join(", ", m.GetParameters().Select(p => $"{p.ParameterType} {p.Name}"));
                        Console.WriteLine($"  - {m.ReturnType} {m.Name}({parameters})");
                    }
                }
            }
        } catch (Exception ex) {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
