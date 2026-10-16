using MealPlanner.Benchmark.Experiments;

string which = args.Length > 0 ? args[0].ToLowerInvariant() : "all";
string outDir = args.Length > 1 ? args[1] : "results";

bool Want(string name) => which == "all" || which == name;

if (Want("menu")) MenuExperiment.Run(repeats: 10, outDir);
if (Want("recommend")) RecommendationExperiment.Run(households: 1000, dishCount: 300, outDir);
if (Want("substitute")) SubstitutionExperiment.Run(trials: 5000, dishCount: 300, outDir);
if (Want("pantry")) PantryMenuExperiment.Run(householdsPerSize: 100, outDir);

Console.WriteLine($"\nCSV збережено в каталозі: {Path.GetFullPath(outDir)}");
