namespace TypeScript.Compiler.Tests;

/// <summary>
/// <c>performance.ts</c>：测量在显式开启前不记录任何东西，计时器按最外层进出对打点。
/// </summary>
/// <remarks>
/// <see cref="Performance"/> 是全局静态状态，所以每个用例都用 try/finally 收尾 ——
/// xUnit 会并行跑不同的测试类，漏掉清理会污染其他用例。
/// </remarks>
public class PerformanceTests
{
    [Fact]
    public void Measurements_AreOffUntilEnabled()
    {
        Performance.Disable();
        try
        {
            Assert.False(Performance.IsEnabled);

            Performance.Mark("mark");
            Performance.Measure("measure", "mark", "mark");

            Assert.Equal(0, Performance.GetCount("mark"));
            Assert.Equal(0d, Performance.GetDuration("measure"));
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void Enable_IsIdempotentAndReturnsTrue()
    {
        Performance.Disable();
        try
        {
            Assert.True(Performance.Enable());
            Assert.True(Performance.IsEnabled);
            Assert.True(Performance.Enable());
            Assert.True(Performance.IsEnabled);
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void Mark_CountsRepeatedMarks()
    {
        Performance.Disable();
        try
        {
            Performance.Enable();
            Performance.Mark("a");
            Performance.Mark("a");
            Performance.Mark("b");

            Assert.Equal(2, Performance.GetCount("a"));
            Assert.Equal(1, Performance.GetCount("b"));
            Assert.Equal(0, Performance.GetCount("never-marked"));
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void Timer_OnlyMarksWhenTheOutermostPairCloses()
    {
        Performance.Disable();
        try
        {
            Performance.Enable();
            var timer = Performance.CreateTimer("m", "start", "end");

            timer.Enter();
            timer.Enter();
            Assert.Equal(1, Performance.GetCount("start"));

            timer.Exit(); // 仍在嵌套内 —— 不打 end
            Assert.Equal(0, Performance.GetCount("end"));

            timer.Exit(); // 退出最外层 —— 打 end 并记录一次测量
            Assert.Equal(1, Performance.GetCount("end"));

            var recorded = new List<string>();
            Performance.ForEachMeasure((name, duration) =>
            {
                Assert.True(duration >= 0d);
                recorded.Add(name);
            });
            Assert.Contains("m", recorded);
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void CreateTimerIf_FalseYieldsTheSharedNoOpTimer()
    {
        var timer = Performance.CreateTimerIf(false, "m", "s", "e");
        Assert.Same(Performance.NullTimerInstance, timer);
    }

    [Fact]
    public void CreateTimerIf_TrueYieldsARealTimer()
    {
        var timer = Performance.CreateTimerIf(true, "m", "s", "e");
        Assert.NotSame(Performance.NullTimerInstance, timer);
    }

    [Fact]
    public void ForEachMeasureAndMark_VisitEverythingRecorded()
    {
        Performance.Disable();
        try
        {
            Performance.Enable();
            Performance.Mark("m1");
            Performance.Mark("m2");
            Performance.Measure("d1", "m1", "m2");

            var marks = new List<string>();
            Performance.ForEachMark(marks.Add);
            Assert.Contains("m1", marks);

            var measures = new List<string>();
            Performance.ForEachMeasure((name, _) => measures.Add(name));
            Assert.Contains("d1", measures);
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void ClearMeasures_RemovesOneOrAll()
    {
        Performance.Disable();
        try
        {
            Performance.Enable();
            Performance.Mark("start");
            Performance.Mark("end");

            // start/end 必须是不同的 mark，否则 end - start 恒为 0，断言就失去意义。
            Performance.Measure("d1", "start", "end");
            Performance.Measure("d2", "start", "end");

            Performance.ClearMeasures("d1");
            Assert.Equal(0d, Performance.GetDuration("d1"));
            Assert.NotEqual(0d, Performance.GetDuration("d2"));

            Performance.ClearMeasures();
            Assert.Equal(0d, Performance.GetDuration("d2"));
        }
        finally
        {
            Performance.Disable();
        }
    }

    [Fact]
    public void Disable_DropsEverythingCollected()
    {
        Performance.Disable();
        try
        {
            Performance.Enable();
            Performance.Mark("m");
            Assert.Equal(1, Performance.GetCount("m"));

            Performance.Disable();

            Assert.False(Performance.IsEnabled);
            Assert.Equal(0, Performance.GetCount("m"));
        }
        finally
        {
            Performance.Disable();
        }
    }
}
