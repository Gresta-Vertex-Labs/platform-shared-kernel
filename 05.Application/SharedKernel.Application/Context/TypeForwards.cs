using System.Runtime.CompilerServices;
using SharedKernel.Application.Context;

// IRequestContext, SystemRequestContext and AnonymousRequestContext moved to
// SharedKernel.Application.Abstractions (a MediatR-free package 06.Persistence also references). The
// namespace is unchanged, so source that imports SharedKernel.Application.Context keeps compiling, and
// these forwards keep assemblies compiled against the old location binding at run time.
[assembly: TypeForwardedTo(typeof(IRequestContext))]
[assembly: TypeForwardedTo(typeof(SystemRequestContext))]
[assembly: TypeForwardedTo(typeof(AnonymousRequestContext))]
