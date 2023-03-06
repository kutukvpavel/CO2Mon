using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CO2Mon.Models;

using CsvHelper;
using ScottPlot;

namespace CO2Mon;

public static class StorageProvider
{
    private static async Task CsvEngine(CsvWriter w, IEnumerable<List<Coordinates>> src)
    {
        List<Tuple<List<Coordinates>, DateTime>> srcWithStarts = 
            src.Where(x => x.Count > 0)
            .Select(x => new Tuple<List<Coordinates>, DateTime>(x, DateTime.FromOADate(x[0].X))).ToList();
        for (int i = 0; i < srcWithStarts.Min(x => x.Item1.Count); i++)
        {
            foreach (var item in srcWithStarts)
            {
                var curDt = DateTime.FromOADate(item.Item1[i].X);
                w.WriteField(curDt.ToString(DateTimeFormat));
                w.WriteField((curDt - item.Item2).TotalSeconds);
                w.WriteField(item.Item1[i].Y);
            }
            await w.NextRecordAsync();
        }
    }

    public static string DateTimeFormat {get;set;} = "yyyy.MM.dd HH:mm:ss.ff";
    public static async Task StoreAsCsv(MH_Z19B ctrl, string filePath)
    {
        using TextWriter t = new StreamWriter(filePath);
        using CsvWriter w = new(t, CultureInfo.InvariantCulture);
        w.WriteField("DateTime");
        w.WriteField("TotalSeconds");
        w.WriteField("Unlim (PPM)");
        w.WriteField("DateTime");
        w.WriteField("TotalSeconds");
        w.WriteField("Lim PPM");
        await w.NextRecordAsync();
        await CsvEngine(w, new List<Coordinates>[] { ctrl.PointsUnlimited, ctrl.PointsLimited });
    }
}