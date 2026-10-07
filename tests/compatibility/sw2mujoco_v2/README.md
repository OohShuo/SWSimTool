# 固定旧版 v2 样本

由更名前 d19fbb3 对应的生产 SW2URDF.dll 生成，未使用用户 CAD 工程。此目录的 JSON/XML 不能跟随产品更名批量替换。

覆盖 URDF 树、惯量、非主轴关节、局部位姿、JointCadReference、configuration/link/joint/site/sensor/actuator/equality/force ID、IMU、connect、弹簧与碰撞模式。provenance.json 固定样本哈希；兼容测试必须读取这些旧字节，不能用新导出器重新生成替代。
