/* Start CoreCLR without its global diagnostic sockets. No persistent settings. */
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>
#ifdef __APPLE__
#include <mach-o/dyld.h>
#endif
int main(int argc, char **argv) {
    (void)argc;
    char executable[PATH_MAX], resolved[PATH_MAX], host[PATH_MAX];
#ifdef __APPLE__
    uint32_t size = sizeof(executable);
    if (_NSGetExecutablePath(executable, &size) != 0) return 2;
#else
    ssize_t size = readlink("/proc/self/exe", executable, sizeof(executable) - 1);
    if (size < 0 || (size_t)size >= sizeof(executable) - 1) return 2;
    executable[size] = '\0';
#endif
    if (!realpath(executable, resolved)) return 2;
    char *separator = strrchr(resolved, '/');
    if (!separator) return 2;
    *separator = '\0';
    if (snprintf(host, sizeof(host), "%s/.deleguard-host", resolved) >= (int)sizeof(host)) return 2;
    if (setenv("DOTNET_EnableDiagnostics", "0", 1) != 0) return 2;
    argv[0] = host;
    execv(host, argv);
    fputs("DeleGuard could not start. Keep the complete portable package together.\n", stderr);
    return 2;
}
