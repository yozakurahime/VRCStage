using System;
using System.Collections.Generic;
using System.Diagnostics;
namespace AvatarLab
{
    public static class LabPerformance
    {
        public static bool Enabled;
        public sealed class Counter { public double milliseconds; public int calls; }
        public static readonly Dictionary<string,Counter> Counters=new Dictionary<string,Counter>();
        public static Scope Measure(string name)=>new Scope(name);
        public struct Scope : IDisposable
        {
            readonly string name; readonly long start;
            public Scope(string name) { this.name=name; start=Enabled?Stopwatch.GetTimestamp():0; }
            public void Dispose()
            {
                if(start==0) return;
                if(!Counters.TryGetValue(name,out var c)) { c=new Counter(); Counters[name]=c; }
                c.milliseconds+=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency; c.calls++;
            }
        }
    }
}
