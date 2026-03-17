import { dotnet } from "./_framework/dotnet.js";

globalThis.__nativeWebViewIntegration = {
  lastMessage: null,
  publish(message) {
    this.lastMessage = message;
    globalThis.__nativeWebViewIntegrationResult = message;
    console.log(message);
  }
};

const runtime = await dotnet
  .withDiagnosticTracing(false)
  .withApplicationArgumentsFromQuery()
  .create();

const config = runtime.getConfig();
await runtime.runMain(config.mainAssemblyName, [globalThis.location.href]);
