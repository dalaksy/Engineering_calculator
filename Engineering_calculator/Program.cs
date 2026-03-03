using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace EngineeringCalculator
{
    public class Calculator
    {
        public double Plus(double a, double b) => a + b;
        public double Minus(double a, double b) => a - b;
        public double Multiply(double a, double b) => a * b;
        public double Divide(double a, double b) => b != 0 ? a / b : double.NaN;
        public double Power(double a, double b) => Math.Pow(a, b);
        public double Sqrt(double a) => a >= 0 ? Math.Sqrt(a) : double.NaN;
        public double Abs(double a) => Math.Abs(a);
    }

    public class DatabaseWork
    {
        private string connectionString = "Data Source=calculator.db";

        public void InitDatabase()
        {
            using (var connection = new SqliteConnection(connectionString))
            {
                connection.Open();
                string sql = @"CREATE TABLE IF NOT EXISTS history (
                                id INTEGER PRIMARY KEY AUTOINCREMENT,
                                expression TEXT,
                                res REAL,
                                dt DATETIME)";
                using (var command = new SqliteCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                }
            }
        }

        public async Task SaveToDatabase(string expression, double result)
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = "INSERT INTO history (expression, res, dt) VALUES (@exp, @r, datetime('now'))";
                    using (var command = new SqliteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@exp", expression);
                        command.Parameters.AddWithValue("@r", double.IsInfinity(result) || double.IsNaN(result) ? 0 : result);
                        await command.ExecuteNonQueryAsync();
                    }
                }
            }
            catch { }
        }

        public async Task PrintHistoryAsync()
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = "SELECT expression, res, dt FROM history ORDER BY dt ASC";
                    using (var command = new SqliteCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            Console.WriteLine("\n--- CALCULATION HISTORY ---");
                            while (await reader.ReadAsync())
                            {
                                string date = reader.GetString(2);
                                string exp = reader.GetString(0);
                                double res = reader.GetDouble(1);
                                Console.WriteLine($"{date} | {exp} = {res}");
                            }
                            Console.WriteLine("---------------------------\n");
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Error reading history: " + ex.Message); }
        }

        public async Task ClearHistoryAsync()
        {
            try
            {
                using (var connection = new SqliteConnection(connectionString))
                {
                    await connection.OpenAsync();
                    using (var command = new SqliteCommand("DELETE FROM history", connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("Database history has been cleared.");
                }
            }
            catch (Exception ex) { Console.WriteLine("Error clearing history: " + ex.Message); }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            DatabaseWork db = new DatabaseWork();
            DataTable dt = new DataTable();
            Calculator calc = new Calculator();

            db.InitDatabase();

            double lastResult = 0;
            bool isFirstRun = true;

            Console.WriteLine("Engineering Calculator (Console)");
            Console.WriteLine("Type 'info' for help.");

            while (true)
            {
                try
                {
                    Console.Write(isFirstRun ? "\nEnter expression: " : $"\n[{lastResult}] What's next? ");
                    string input = Console.ReadLine()?.Trim().ToLower();

                    if (string.IsNullOrEmpty(input)) continue;
                    if (input == "exit") break;

                    if (input == "info")
                    {
                        Console.WriteLine("\nOPERATIONS: + , - , * , / , ( ) , abs() , sqrt() , pow(a,b)");
                        Console.WriteLine("COMMANDS:");
                        Console.WriteLine(" hstr  - Show history");
                        Console.WriteLine(" clear - Clear database history");
                        Console.WriteLine(" c     - Reset current result");
                        Console.WriteLine(" exit  - Exit program");
                        continue;
                    }

                    if (input == "hstr") { await db.PrintHistoryAsync(); continue; }
                    if (input == "clear") { await db.ClearHistoryAsync(); continue; }
                    if (input == "c") { isFirstRun = true; lastResult = 0; continue; }

                    string expression = input;
                    if (!isFirstRun && (input.StartsWith("+") || input.StartsWith("-") || input.StartsWith("*") || input.StartsWith("/")))
                    {
                        expression = lastResult.ToString(CultureInfo.InvariantCulture) + input;
                    }

                    string processedExpression = ProcessMathFunctions(expression, calc, dt);
                    processedExpression = processedExpression.Replace(",", ".");

                    var resultValue = dt.Compute(processedExpression, "");
                    double finalResult = Convert.ToDouble(resultValue);

                    if (double.IsInfinity(finalResult))
                    {
                        Console.WriteLine("Error: Number is too large");
                        lastResult = 0;
                        isFirstRun = true;
                    }
                    else if (double.IsNaN(finalResult))
                    {
                        Console.WriteLine("Error: Calculation impossible (NaN)");
                        lastResult = 0;
                        isFirstRun = true;
                    }
                    else
                    {
                        lastResult = finalResult;
                        Console.WriteLine($"= {lastResult}");
                        await db.SaveToDatabase(expression, lastResult);
                        isFirstRun = false;
                    }
                }
                catch (Exception)
                {
                    Console.WriteLine("Error: Invalid input or calculation error.");
                    isFirstRun = true;
                }
            }
        }

        static string ProcessMathFunctions(string input, Calculator calc, DataTable dt)
        {
            string output = input;
            while (Regex.IsMatch(output, @"(sqrt|abs|pow)\("))
            {
                output = Regex.Replace(output, @"pow\(([^(),]+),([^(),]+)\)", m => {
                    double a = Convert.ToDouble(dt.Compute(m.Groups[1].Value.Replace(",", "."), ""));
                    double b = Convert.ToDouble(dt.Compute(m.Groups[2].Value.Replace(",", "."), ""));
                    return calc.Power(a, b).ToString(CultureInfo.InvariantCulture);
                });

                output = Regex.Replace(output, @"sqrt\(([^()]+)\)", m => {
                    double val = Convert.ToDouble(dt.Compute(m.Groups[1].Value.Replace(",", "."), ""));
                    return calc.Sqrt(val).ToString(CultureInfo.InvariantCulture);
                });

                output = Regex.Replace(output, @"abs\(([^()]+)\)", m => {
                    double val = Convert.ToDouble(dt.Compute(m.Groups[1].Value.Replace(",", "."), ""));
                    return calc.Abs(val).ToString(CultureInfo.InvariantCulture);
                });
            }
            return output;
        }
    }
}