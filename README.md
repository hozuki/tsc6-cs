注：本项目基于 TS6（TS）移植。基于 TS7（tsgo） 的移植见 <https://github.com/hozuki/tsc7-cs>。

全程使用 DeepSeek 4.0 Flash，无人工代码。目的是测试成本和AI代码产出质量，以群友为基准。这个repo从零到当前状态总计使用了大约380元的API开销，直接使用官方API。

实测表现很差（conformance 测试时间居然是 TS6 的4倍）。原因是鲸鱼娘并不懂如何以C#的思维移植TS；TS的的联合类型也造成了很大的麻烦。

# tsc-cs

Porting TypeScript compiler (TSC) to C#.
