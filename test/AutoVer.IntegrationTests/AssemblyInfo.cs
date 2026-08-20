// Integration tests exercise real git repos, subprocesses, and process-wide Console
// redirection (see AutoVerUtilities.RunCapturingOutput). Running them serially avoids
// any risk of cross-test interference from that shared state.
[assembly: NotInParallel]
