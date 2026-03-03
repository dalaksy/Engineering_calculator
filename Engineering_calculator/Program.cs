using System;
using System.Data;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Npgsql;

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
        private string connectionString = "Host=localhost;Username=postgres;Password=password1234admin;Database=my_project";

        public async Task SaveToDatabase(string expression, double result)
        {
            try
            {
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = "INSERT INTO history (expression, res, dt) VALUES (@exp, @r, CURRENT_TIMESTAMP)";
                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("exp", expression);
                        command.Parameters.AddWithValue("r", double.IsInfinity(result) || double.IsNaN(result) ? 0 : result);
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
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = "SELECT expression, res, dt FROM history ORDER BY dt ASC";
                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            Console.WriteLine("\n--- ИСТОРИЯ ---");
                            while (await reader.ReadAsync())
                            {
                                Console.WriteLine($"{reader.GetDateTime(2):HH:mm:ss} | {reader.GetString(0)} = {reader.GetDouble(1)}");
                            }
                            Console.WriteLine("---------------\n");
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Ошибка чтения истории: " + ex.Message); }
        }

        public async Task ClearHistoryAsync()
        {
            try
            {
                using (var connection = new NpgsqlConnection(connectionString))
                {
                    await connection.OpenAsync();
                    string sql = "DELETE FROM history";
                    using (var command = new NpgsqlCommand(sql, connection))
                    {
                        await command.ExecuteNonQueryAsync();
                    }
                    Console.WriteLine("История в базе данных очищена.");
                }
            }
            catch (Exception ex) { Console.WriteLine("Ошибка очистки: " + ex.Message); }
        }
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            DatabaseWork db = new DatabaseWork();
            DataTable dt = new DataTable();
            Calculator calc = new Calculator();

            double lastResult = 0;
            bool isFirstRun = true;

            Console.WriteLine("--- Инженерный Калькулятор 3.0 ---");
            Console.WriteLine("Введите 'info' для справки.");

            while (true)
            {
                try
                {
                    Console.Write(isFirstRun ? "\nВведите выражение: " : $"\n[{lastResult}] Что дальше? ");
                    string input = Console.ReadLine()?.Trim().ToLower();

                    if (string.IsNullOrEmpty(input)) continue;
                    if (input == "exit") break;

                    if (input == "info")
                    {
                        Console.WriteLine("\nОПЕРАЦИИ: + , - , * , / , ( ) , abs() , sqrt() , pow(a,b)");
                        Console.WriteLine("КОМАНДЫ:");
                        Console.WriteLine(" hstr  - Показать историю");
                        Console.WriteLine(" clear - Очистить историю в БД");
                        Console.WriteLine(" c     - Сбросить текущий результат");
                        Console.WriteLine(" exit  - Выход");
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
                        Console.WriteLine("Ошибка: Слишком большое число");
                        lastResult = 0;
                        isFirstRun = true;
                    }
                    else if (double.IsNaN(finalResult))
                    {
                        Console.WriteLine("Ошибка: Невозможно вычислить");
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
                catch (Exception ex)
                {
                    Console.WriteLine("Ошибка: Некорректный ввод или ошибка вычисления.");
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