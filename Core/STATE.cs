namespace RT950Update.Core;

internal enum STATE
{
    HandShakeStep0_0,
    HandShakeStep0_1,
    HandShakeStep0_2,
    HandShakeStep0_3,
    HandShake1,
    HandShake2,
    HandShake3,
    HandShake4,
    Booting_IntoBootMode,
    Booting_CheckModelType,
    Booting_SendPackages,
    Booting_SendData,
    Booting_ReadFile,
    Booting_End,
    Booting_WaitResponse1,
    Booting_WaitResponse2,
    Booting_WaitResponse3,
    Booting_WaitResponse4
}
