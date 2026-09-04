---
name: feedback-verify-cross-domain-source
description: Read another domain's actual source before describing its behavior in a design narrative or XML doc, rather than trusting a prior phase's/plan's characterization of it
type: feedback
---

While implementing P-444 (`SharedKernel.Validation.FluentValidation`), the phase's own design
narrative (and this agent's own first XML-doc draft) asserted that
`05.Application.Behaviors.Validation.ValidationBehavior` reads FluentValidation's
`ValidationFailure.ErrorCode` when projecting a failure into the platform's `Error` type. Reading
the actual source (`05.Application/SharedKernel.Application.Behaviors/Validation/ValidationBehavior.cs`)
showed this was wrong: it projects `Error.Validation(failure.PropertyName, failure.ErrorMessage)` —
the FluentValidation **property name** becomes the downstream `Error.Code`, not `ErrorCode`. The
XML docs and both READMEs were corrected before shipping instead of documenting the mistaken
assumption as fact.

**Why:** a phase spec or an earlier design-lock pass describes a cross-domain type's behavior as
understood *at the time it was written* — it can drift, be simplified for readability, or simply be
wrong. When a phase's own documentation deliverable (an XML-doc composition recipe, a README
usage note) makes a specific factual claim about another domain's type, verify it against that
type's real source before shipping the claim, especially when the claim is easy to state precisely
(a one-line grep away) and would otherwise ship an inaccurate contract that a downstream consumer
might rely on.

**How to apply:** whenever a task in this domain asks for a "composition recipe" or interop
documentation referencing a specific type/method in another domain, grep/Read that type's actual
`.cs` file before writing the doc — don't paraphrase from the phase spec's own description of it.
This is a specific instance of a more general habit worth carrying into every future 01.Core
session: cross-domain claims in docs are load-bearing for consumers and cheap to verify.
