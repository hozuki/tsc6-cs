#nullable enable

namespace TypeScript.Compiler;

// ported from: src/compiler/transformers/es2015.ts:3310-3375, :4092-4195, :4308-4840 — computed property names,
// array/call/new spread, template literals, `super` and `new.target`.
/// <summary>
/// The expression half of the ES2015 downlevel transform.
/// </summary>
internal sealed partial class ES2015Transformer
{
    /// <summary>ported from: src/compiler/transformers/es2015.ts:3310-3373 — <c>visitObjectLiteralExpression</c>.</summary>
    /// <remarks>PORT-NOTE: the <c>hasComputed = …</c> assignment inside the loop condition is part of the reference's
    /// short-circuit (<c>||</c> evaluates the left arm first); C# keeps the same order and the same side effect.</remarks>
    private object? VisitObjectLiteralExpression(ObjectLiteralExpression node)
    {
        var properties = node.Properties;

        // Find the first computed property.
        // Everything until that point can be emitted as part of the initial object literal.
        var numInitialProperties = -1;
        var hasComputed = false;
        for (var i = 0; i < properties!.Count; i++)
        {
            var property = properties[i];
            if (((property.TransformFlags & TransformFlags.ContainsYield) != 0 && (_hierarchyFacts & HierarchyFacts.AsyncFunctionBody) != 0)
                || (hasComputed = Debug.CheckDefined(((INamedDeclaration)property).Name!).Kind == SyntaxKind.ComputedPropertyName))
            {
                numInitialProperties = i;
                break;
            }
        }

        if (numInitialProperties < 0)
        {
            return Utilities.VisitEachChildWorker(node, Visitor, _context);
        }

        // For computed properties, we need to create a unique handle to the object
        // literal so we can modify it without risking internal assignments tainting the object.
        var temp = _factory.CreateTempVariable(_context.HoistVariableDeclaration, /*reservedInNestedScopes*/ null);

        // Write out the first non-computed properties, then emit the rest through indexing on the temp variable.
        var expressions = new List<Expression>();
        var assignment = _factory.CreateAssignment(
            temp,
            EmitNodes.SetEmitFlags(
                _factory.CreateObjectLiteralExpression(
                    Utilities.VisitNodes(properties, Visitor, Utilities.IsObjectLiteralElementLike, 0, numInitialProperties),
                    node.MultiLine),
                hasComputed ? EmitFlags.Indented : EmitFlags.None));

        if (node.MultiLine == true)
        {
            FactoryUtilities.StartOnNewLine(assignment);
        }

        expressions.Add(assignment);

        AddObjectLiteralMembers(expressions, node, temp, numInitialProperties);

        // We need to clone the temporary identifier so that we can write it on a
        // new line
        expressions.Add(node.MultiLine == true
            ? FactoryUtilities.StartOnNewLine(Utilities.SetParent(Utilities.SetTextRange(_factory.CloneNode(temp), temp), temp.Parent)!)
            : temp);
        return _factory.InlineExpressions(expressions);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4092-4127 — <c>addObjectLiteralMembers</c>.</summary>
    private void AddObjectLiteralMembers(List<Expression> expressions, ObjectLiteralExpression node, Identifier receiver, int start)
    {
        var properties = node.Properties!;
        var numProperties = properties.Count;
        for (var i = start; i < numProperties; i++)
        {
            var property = properties[i];
            switch (property.Kind)
            {
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                    var accessors = ES2015TransformSupport.GetAllAccessorDeclarations(properties, (IFunctionLikeDeclarationBase)property);
                    if (ReferenceEquals(property, accessors.FirstAccessor))
                    {
                        expressions.Add(TransformAccessorsToExpression(receiver, accessors, node, node.MultiLine == true));
                    }

                    break;

                case SyntaxKind.MethodDeclaration:
                    expressions.Add(TransformObjectLiteralMethodDeclarationToExpression((MethodDeclaration)property, receiver, node, node.MultiLine == true));
                    break;

                case SyntaxKind.PropertyAssignment:
                    expressions.Add(TransformPropertyAssignmentToExpression((PropertyAssignment)property, receiver, node.MultiLine == true));
                    break;

                case SyntaxKind.ShorthandPropertyAssignment:
                    expressions.Add(TransformShorthandPropertyAssignmentToExpression((ShorthandPropertyAssignment)property, receiver, node.MultiLine == true));
                    break;

                default:
                    Debug.FailBadSyntaxKind(node);
                    break;
            }
        }
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4133-4154 — <c>transformPropertyAssignmentToExpression</c>.</summary>
    private Expression TransformPropertyAssignmentToExpression(PropertyAssignment property, Expression receiver, bool startsOnNewLine)
    {
        var expression = _factory.CreateAssignment(
            FactoryUtilities.CreateMemberAccessForPropertyName(
                _factory,
                receiver,
                Debug.CheckDefined(Utilities.VisitNode(property.Name, Visitor, Utilities.IsPropertyName))),
            Debug.CheckDefined(Utilities.VisitNode(property.Initializer, Visitor, Utilities.IsExpression)));
        Utilities.SetTextRange(expression, property);
        if (startsOnNewLine)
        {
            FactoryUtilities.StartOnNewLine(expression);
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4156-4177 — <c>transformShorthandPropertyAssignmentToExpression</c>.</summary>
    private Expression TransformShorthandPropertyAssignmentToExpression(ShorthandPropertyAssignment property, Expression receiver, bool startsOnNewLine)
    {
        var expression = _factory.CreateAssignment(
            FactoryUtilities.CreateMemberAccessForPropertyName(
                _factory,
                receiver,
                Debug.CheckDefined(Utilities.VisitNode(property.Name, Visitor, Utilities.IsPropertyName))),
            _factory.CloneNode(property.Name));
        Utilities.SetTextRange(expression, property);
        if (startsOnNewLine)
        {
            FactoryUtilities.StartOnNewLine(expression);
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4179-4193 — <c>transformObjectLiteralMethodDeclarationToExpression</c>.</summary>
    private Expression TransformObjectLiteralMethodDeclarationToExpression(MethodDeclaration method, Expression receiver, Node container, bool startsOnNewLine)
    {
        var expression = _factory.CreateAssignment(
            FactoryUtilities.CreateMemberAccessForPropertyName(
                _factory,
                receiver,
                Debug.CheckDefined(Utilities.VisitNode(method.Name, Visitor, Utilities.IsPropertyName))),
            TransformFunctionLikeToExpression(method, /*location*/ method, /*name*/ null, container));
        Utilities.SetTextRange(expression, method);
        if (startsOnNewLine)
        {
            FactoryUtilities.StartOnNewLine(expression);
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4308-4319 — <c>visitArrayLiteralExpression</c>.</summary>
    private Expression VisitArrayLiteralExpression(ArrayLiteralExpression node)
    {
        if (Core.Some(node.Elements, (e, _) => NodeTests.IsSpreadElement(e)))
        {
            // We are here because we contain a SpreadElementExpression.
            return TransformAndSpreadElements(
                node.Elements!,
                /*isArgumentList*/ false,
                node.MultiLine == true,
                /*hasTrailingComma*/ node.Elements!.HasTrailingComma);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4321-4341 — <c>visitCallExpression</c>.</summary>
    private object? VisitCallExpression(CallExpression node)
    {
        if ((ES2015TransformSupport.GetInternalEmitFlags(node) & InternalEmitFlags.TypeScriptClassWrapper) != 0)
        {
            return VisitTypeScriptClassWrapper(node);
        }

        var expression = FactoryUtilities.SkipOuterExpressions(node.Expression!);
        if (expression.Kind == SyntaxKind.SuperKeyword
            || Utilities.IsSuperProperty(expression)
            || Core.Some(node.Arguments, (e, _) => NodeTests.IsSpreadElement(e)))
        {
            return VisitCallExpressionWithPotentialCapturedThisAssignment(node, /*assignToCapturedThis*/ true);
        }

        return _factory.UpdateCallExpression(
            node,
            Debug.CheckDefined(Utilities.VisitNode(node.Expression, CallExpressionVisitor, Utilities.IsExpression)),
            /*typeArguments*/ null,
            Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression));
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4343-4520 — <c>visitTypeScriptClassWrapper</c>.</summary>
    /// <remarks>PORT-NOTE: <c>tryCast</c>/<c>cast</c> are the <c>is</c>/explicit casts the model's classes license;
    /// <c>filter</c> is <see cref="Core.Filter{T}"/> materialized to a list.</remarks>
    private object? VisitTypeScriptClassWrapper(CallExpression node)
    {
        // We skip any outer expressions in a number of places to get to the innermost
        // expression, but we will restore them later to preserve comments and source maps.
        var body = (Block)((ArrowFunction)FactoryUtilities.SkipOuterExpressions(node.Expression!)).Body!;

        // The class statements are the statements generated by visiting the first statement with initializer of the
        // body (1), while all other statements are added to remainingStatements (2)
        bool IsVariableStatementWithInitializer(IStatement stmt)
            => NodeTests.IsVariableStatement((Node)stmt)
                && Core.First(((VariableStatement)stmt).DeclarationList!.Declarations!).Initializer is not null;

        // visit the class body statements outside of any converted loop body.
        var savedConvertedLoopState = _convertedLoopState;
        _convertedLoopState = null;
        var bodyStatements = Utilities.VisitNodes(body.Statements, ClassWrapperStatementVisitor, Utilities.IsStatement);
        _convertedLoopState = savedConvertedLoopState;

        var classStatements = (Core.Filter(bodyStatements, IsVariableStatementWithInitializer) ?? []).ToList();
        var remainingStatements = (Core.Filter(bodyStatements, (IStatement stmt) => !IsVariableStatementWithInitializer(stmt)) ?? []).ToList();
        var varStatement = (VariableStatement)Core.First(classStatements);

        // We know there is only one variable declaration here as we verified this in an
        // earlier call to isTypeScriptClassWrapper
        var variable = varStatement.DeclarationList!.Declarations![0];
        var initializer = FactoryUtilities.SkipOuterExpressions(variable.Initializer!);

        // Under certain conditions, the 'ts' transformer may introduce a class alias, which
        // we see as an assignment, for example:
        //
        //  (function () {
        //      var C_1;
        //      var C = C_1 = (function () {
        //          function C() {
        //          }
        //          C.x = function () { return C_1; }
        //          return C;
        //      }());
        //      C = C_1 = __decorate([dec], C);
        //      return C;
        //  }())
        //
        var aliasAssignment = Utilities.IsAssignmentExpression(initializer) ? (BinaryExpression)initializer : null;
        if (aliasAssignment is null
            && NodeTests.IsBinaryExpression(initializer)
            && ((BinaryExpression)initializer).OperatorToken!.Kind == SyntaxKind.CommaToken)
        {
            aliasAssignment = Utilities.IsAssignmentExpression(((BinaryExpression)initializer).Left!)
                ? (BinaryExpression)((BinaryExpression)initializer).Left!
                : null;
        }

        // The underlying call (3) is another IIFE that may contain a '_super' argument.
        var call = (CallExpression)FactoryUtilities.SkipOuterExpressions(
            aliasAssignment is not null ? aliasAssignment.Right! : initializer);
        var func = (FunctionExpression)FactoryUtilities.SkipOuterExpressions(call.Expression!);

        var funcStatements = func.Body!.Statements!;
        var classBodyStart = 0;
        var classBodyEnd = -1;

        var statements = new List<IStatement>();
        if (aliasAssignment is not null)
        {
            // If we have a class alias assignment, we need to move it to the down-level constructor
            // function we generated for the class.
            var extendsCall = funcStatements[classBodyStart] is ExpressionStatement es ? es : null;
            if (extendsCall is not null)
            {
                statements.Add(extendsCall);
                classBodyStart++;
            }

            // The next statement is the function declaration.
            statements.Add(funcStatements[classBodyStart]);
            classBodyStart++;

            // Add the class alias following the declaration.
            statements.Add(
                _factory.CreateExpressionStatement(
                    _factory.CreateAssignment(
                        aliasAssignment.Left,
                        (Identifier)variable.Name!)));
        }

        // Find the trailing 'return' statement (4)
        while (!NodeTests.IsReturnStatement((Node)Core.ElementAt<IStatement>(funcStatements, classBodyEnd)!))
        {
            classBodyEnd--;
        }

        // When we extract the statements of the inner IIFE, we exclude the 'return' statement (4)
        // as we already have one that has been introduced by the 'ts' transformer.
        Core.AddRange(statements, funcStatements, classBodyStart, classBodyEnd);

        if (classBodyEnd < -1)
        {
            // If there were any hoisted declarations following the return statement, we should
            // append them.
            Core.AddRange(statements, funcStatements, classBodyEnd + 1);
        }

        // TODO(rbuckton): We should consider either improving the inlining here, or remove it entirely, since
        //                 the new esDecorators emit doesn't inline.

        // Add the remaining statements of the outer wrapper. Use the 'return' statement
        // of the inner wrapper if its expression is not trivially an Identifier.
        var returnStatement = Core.ElementAt<IStatement>(funcStatements, classBodyEnd) as ReturnStatement;
        foreach (var statement in remainingStatements)
        {
            if (NodeTests.IsReturnStatement((Node)statement)
                && returnStatement?.Expression is not null
                && !NodeTests.IsIdentifier(returnStatement.Expression))
            {
                statements.Add(returnStatement);
            }
            else
            {
                statements.Add(statement);
            }
        }

        // The 'es2015' class transform may add an end-of-declaration marker. If so we will add it
        // after the remaining statements from the 'ts' transformer.
        Core.AddRange(statements, classStatements, /*start*/ 1);

        // Recreate any outer parentheses or partially-emitted expressions to preserve source map
        // and comment locations.
        return _factory.RestoreOuterExpressions(
            node.Expression,
            _factory.RestoreOuterExpressions(
                variable.Initializer,
                _factory.RestoreOuterExpressions(
                    aliasAssignment is not null ? aliasAssignment.Right : null,
                    _factory.UpdateCallExpression(
                        call,
                        _factory.RestoreOuterExpressions(
                            call.Expression,
                            _factory.UpdateFunctionExpression(
                                func,
                                /*modifiers*/ null,
                                /*asteriskToken*/ null,
                                /*name*/ null,
                                /*typeParameters*/ null,
                                func.Parameters,
                                /*type*/ null,
                                _factory.UpdateBlock(
                                    func.Body!,
                                    statements)),
                            /*kinds*/ null),
                        /*typeArguments*/ null,
                        call.Arguments),
                    /*kinds*/ null),
                /*kinds*/ null),
            /*kinds*/ null);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4522-4599 — <c>visitCallExpressionWithPotentialCapturedThisAssignment</c>.</summary>
    private object? VisitCallExpressionWithPotentialCapturedThisAssignment(CallExpression node, bool assignToCapturedThis)
    {
        // We are here either because SuperKeyword was used somewhere in the expression, or
        // because we contain a SpreadElementExpression.
        if ((node.TransformFlags & TransformFlags.ContainsRestOrSpread) != 0
            || node.Expression!.Kind == SyntaxKind.SuperKeyword
            || Utilities.IsSuperProperty(FactoryUtilities.SkipOuterExpressions(node.Expression!)))
        {
            var binding = _factory.CreateCallBinding(node.Expression!, _context.HoistVariableDeclaration, /*languageVersion*/ null, /*cacheIdentifiers*/ null);
            var target = binding.Target!;
            var thisArg = binding.ThisArg!;
            if (node.Expression!.Kind == SyntaxKind.SuperKeyword)
            {
                EmitNodes.SetEmitFlags(thisArg, EmitFlags.NoSubstitution);
            }

            Expression resultingCall;
            if ((node.TransformFlags & TransformFlags.ContainsRestOrSpread) != 0)
            {
                // [source]
                //      f(...a, b)
                //      x.m(...a, b)
                //      super(...a, b)
                //      super.m(...a, b) // in static
                //      super.m(...a, b) // in instance
                //
                // [output]
                //      f.apply(void 0, a.concat([b]))
                //      (_a = x).m.apply(_a, a.concat([b]))
                //      _super.apply(this, a.concat([b]))
                //      _super.m.apply(this, a.concat([b]))
                //      _super.prototype.m.apply(this, a.concat([b]))

                resultingCall = _factory.CreateFunctionApplyCall(
                    Debug.CheckDefined(Utilities.VisitNode(target, CallExpressionVisitor, Utilities.IsExpression)),
                    node.Expression!.Kind == SyntaxKind.SuperKeyword
                        ? thisArg
                        : Debug.CheckDefined(Utilities.VisitNode(thisArg, Visitor, Utilities.IsExpression)),
                    TransformAndSpreadElements(node.Arguments!, /*isArgumentList*/ true, /*multiLine*/ false, /*hasTrailingComma*/ false));
            }
            else
            {
                // [source]
                //      super(a)
                //      super.m(a) // in static
                //      super.m(a) // in instance
                //
                // [output]
                //      _super.call(this, a)
                //      _super.m.call(this, a)
                //      _super.prototype.m.call(this, a)
                resultingCall = Utilities.SetTextRange(
                    _factory.CreateFunctionCallCall(
                        Debug.CheckDefined(Utilities.VisitNode(target, CallExpressionVisitor, Utilities.IsExpression)),
                        node.Expression!.Kind == SyntaxKind.SuperKeyword
                            ? thisArg
                            : Debug.CheckDefined(Utilities.VisitNode(thisArg, Visitor, Utilities.IsExpression)),
                        Utilities.VisitNodes(node.Arguments, Visitor, Utilities.IsExpression)),
                    node);
            }

            if (node.Expression!.Kind == SyntaxKind.SuperKeyword)
            {
                var initializer = _factory.CreateLogicalOr(
                    resultingCall,
                    CreateActualThis());
                resultingCall = assignToCapturedThis
                    ? _factory.CreateAssignment(CreateCapturedThis(), initializer)
                    : initializer;
            }
            return NodeFactory.SetOriginalNode(resultingCall, node);
        }

        if (Utilities.IsSuperCall(node))
        {
            _hierarchyFacts |= HierarchyFacts.CapturedLexicalThis;
        }

        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4601-4631 — <c>visitNewExpression</c>.</summary>
    private Expression VisitNewExpression(NewExpression node)
    {
        if (Core.Some(node.Arguments, (e, _) => NodeTests.IsSpreadElement(e)))
        {
            // We are here because we contain a SpreadElementExpression.
            // [source]
            //      new C(...a)
            //
            // [output]
            //      new ((_a = C).bind.apply(_a, [void 0].concat(a)))()

            var binding = _factory.CreateCallBinding(
                _factory.CreatePropertyAccessExpression(node.Expression!, "bind"),
                _context.HoistVariableDeclaration,
                /*languageVersion*/ null,
                /*cacheIdentifiers*/ null);
            return _factory.CreateNewExpression(
                _factory.CreateFunctionApplyCall(
                    Debug.CheckDefined(Utilities.VisitNode(binding.Target!, Visitor, Utilities.IsExpression)),
                    binding.ThisArg,
                    TransformAndSpreadElements(
                        _factory.CreateNodeArray<Expression>([_factory.CreateVoidZero(), .. node.Arguments!], null),
                        /*isArgumentList*/ true,
                        /*multiLine*/ false,
                        /*hasTrailingComma*/ false)),
                /*typeArguments*/ null,
                []);
        }
        return Utilities.VisitEachChildWorker(node, Visitor, _context);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4633-4704 — <c>transformAndSpreadElements</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>flatten&lt;SpreadSegment&gt;(spanMap(…))</c> over a heterogeneous
    /// partition visitor is <see cref="Core.Flatten{T}"/> over <see cref="Core.SpanMap{T,TK,TU}"/>, whose key is the
    /// spread/non-spread discriminator the two partition visitors differ by.</remarks>
    private Expression TransformAndSpreadElements(NodeArray<Expression> elements, bool isArgumentList, bool multiLine, bool hasTrailingComma)
    {
        // When there is no leading SpreadElement:
        //
        // [source]
        //      [a, ...b, c]
        //
        // [output (downlevelIteration)]
        //      __spreadArray(__spreadArray([a], __read(b)), [c])
        //
        // [output]
        //      __spreadArray(__spreadArray([a], b), [c])
        //
        // When there *is* a leading SpreadElement:
        //
        // [source]
        //      [...a, b]
        //
        // [output (downlevelIteration)]
        //      __spreadArray(__spreadArray([], __read(a)), [b])
        //
        // [output]
        //      __spreadArray(__spreadArray([], a), [b])
        //
        // NOTE: We use `isPackedArrayLiteral` below rather than just `isArrayLiteral`
        // because ES2015 spread will replace _missing_ array elements with `undefined`,
        // so we cannot just use an array as is. For example:
        //
        // `[1, ...[2, , 3]]` becomes `[1, 2, undefined, 3]`
        //
        // However, for packed array literals (i.e., an array literal with no OmittedExpression
        // elements), we can use the array as-is.

        // Map spans of spread expressions into their expressions and spans of other
        // expressions into an array literal.
        var numElements = elements.Count;
        var segments = Core.Flatten<SpreadSegment>(
            // As we visit each element, we return one of two functions to use as the "key":
            // - `visitSpanOfSpreads` for one or more contiguous `...` spread expressions, i.e. `...a, ...b` in `[1, 2, ...a, ...b]`
            // - `visitSpanOfNonSpreads` for one or more contiguous non-spread elements, i.e. `1, 2`, in `[1, 2, ...a, ...b]`
            Core.SpanMap(
                elements,
                /*keyfn*/ (node, _) => NodeTests.IsSpreadElement(node) ? 0 : 1,
                /*mapfn*/ (partition, kind, start, end) => kind == 0
                    ? (object?)VisitSpanOfSpreads(partition)
                    : VisitSpanOfNonSpreads(partition, multiLine, hasTrailingComma && end == numElements))!);

        if (segments.Count == 1)
        {
            var firstSegment = segments[0];
            // If we don't need a unique copy, then we are spreading into an argument list for
            // a CallExpression or NewExpression. When using `--downlevelIteration`, we need
            // to coerce this into an array for use with `apply`, so we will use the code path
            // that follows instead.
            if (
                isArgumentList && !Js.IsTruthy(_compilerOptions.DownlevelIteration)
                || ES2015TransformSupport.IsPackedArrayLiteral(firstSegment.Expression) // see NOTE (above)
                || EmitHelpers.IsCallToHelper(firstSegment.Expression, "___spreadArray")
            )
            {
                return firstSegment.Expression;
            }
        }

        var startsWithSpread = segments[0].Kind != SpreadSegmentKind.None;
        Expression expression = startsWithSpread
            ? _factory.CreateArrayLiteralExpression(null, null)
            : segments[0].Expression;
        for (var i = startsWithSpread ? 0 : 1; i < segments.Count; i++)
        {
            var segment = segments[i];
            // If this is for an argument list, it doesn't matter if the array is packed or sparse
            expression = _emitHelpers.CreateSpreadArrayHelper(
                expression,
                segment.Expression,
                segment.Kind == SpreadSegmentKind.UnpackedSpread && !isArgumentList);
        }
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4706-4714 — <c>partitionSpread</c>.</summary>
    /// <remarks>PORT-NOTE: TS returns one of the two partition visitors as the span key; the port's span key is the
    /// discriminator <see cref="TransformAndSpreadElements"/> maps back to the same two visitors.</remarks>
    private List<SpreadSegment> VisitSpanOfSpreads(IList<Expression> chunk)
        => Core.Map(chunk, (e, _) => VisitExpressionOfSpread(e))!;

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4716-4733 — <c>visitExpressionOfSpread</c>.</summary>
    private SpreadSegment VisitExpressionOfSpread(Expression node)
    {
        Debug.AssertNode(node, NodeTests.IsSpreadElement);
        var expression = Utilities.VisitNode(((SpreadElement)node).Expression, Visitor, Utilities.IsExpression);
        Debug.Assert(expression is not null);

        // We don't need to pack already packed array literals, or existing calls to the `__read` helper.
        var isCallToReadHelper = EmitHelpers.IsCallToHelper(expression!, "___read");
        var kind = isCallToReadHelper || ES2015TransformSupport.IsPackedArrayLiteral(expression!)
            ? SpreadSegmentKind.PackedSpread
            : SpreadSegmentKind.UnpackedSpread;

        // We don't need the `__read` helper for array literals. Array packing will be performed by `__spreadArray`.
        if (Js.IsTruthy(_compilerOptions.DownlevelIteration)
            && kind == SpreadSegmentKind.UnpackedSpread
            && !NodeTests.IsArrayLiteralExpression(expression!)
            && !isCallToReadHelper)
        {
            expression = _emitHelpers.CreateReadHelper(expression, /*count*/ null);
            // the `__read` helper returns a packed array, so we don't need to ensure a packed array
            kind = SpreadSegmentKind.PackedSpread;
        }

        return CreateSpreadSegment(kind, expression!);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4735-4744 — <c>visitSpanOfNonSpreads</c>.</summary>
    private SpreadSegment VisitSpanOfNonSpreads(IList<Expression> chunk, bool multiLine, bool hasTrailingComma)
    {
        var expression = _factory.CreateArrayLiteralExpression(
            Utilities.VisitNodes(_factory.CreateNodeArray(chunk, hasTrailingComma), Visitor, Utilities.IsExpression),
            multiLine);

        // We do not pack non-spread segments, this is so that `[1, , ...[2, , 3], , 4]` is properly downleveled to
        // `[1, , 2, undefined, 3, , 4]`. See the NOTE in `transformAndSpreadElements`
        return CreateSpreadSegment(SpreadSegmentKind.None, expression);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4746-4753 — <c>visitSpreadElement</c>.</summary>
    private Expression VisitSpreadElement(SpreadElement node)
        => Debug.CheckDefined(Utilities.VisitNode(node.Expression, Visitor, Utilities.IsExpression));

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4755-4762 — <c>visitTemplateLiteral</c>.</summary>
    /// <remarks>PORT-NOTE: the reference's <c>LiteralExpression</c> covers the no-substitution literal and the three
    /// template fragments; the model's class hierarchy does not (TemplateHead/TemplateMiddle/TemplateTail are
    /// <see cref="Node"/>s), so the port takes their common face <see cref="ITemplateLiteralLikeNode"/>, which is what
    /// the body's one read (<c>text</c>) is declared on.</remarks>
    private LeftHandSideExpression VisitTemplateLiteral(ITemplateLiteralLikeNode node)
        => Utilities.SetTextRange(_factory.CreateStringLiteral(node.Text, null, null), (Node)node);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4764-4774 — <c>visitStringLiteral</c>.</summary>
    private StringLiteral VisitStringLiteral(StringLiteral node)
    {
        if (node.HasExtendedUnicodeEscape == true)
        {
            return Utilities.SetTextRange(_factory.CreateStringLiteral(node.Text, null, null), node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4776-4786 — <c>visitNumericLiteral</c>.</summary>
    private NumericLiteral VisitNumericLiteral(NumericLiteral node)
    {
        if ((node.NumericLiteralFlags & TokenFlags.BinaryOrOctalSpecifier) != 0)
        {
            return Utilities.SetTextRange(_factory.CreateNumericLiteral(node.Text, null), node);
        }
        return node;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4788-4802 — <c>visitTaggedTemplateExpression</c>.</summary>
    private LeftHandSideExpression VisitTaggedTemplateExpression(TaggedTemplateExpression node)
        => TaggedTemplate.ProcessTaggedTemplateExpression(
            _context,
            node,
            Visitor,
            _currentSourceFile!,
            RecordTaggedTemplateString,
            ProcessLevel.All);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4804-4821 — <c>visitTemplateExpression</c>.</summary>
    private Expression VisitTemplateExpression(TemplateExpression node)
    {
        Expression expression = _factory.CreateStringLiteral(node.Head!.Text, null, null);
        foreach (var span in node.TemplateSpans!)
        {
            var args = new List<Expression> { Debug.CheckDefined(Utilities.VisitNode(span.Expression, Visitor, Utilities.IsExpression)) };

            var literalText = ((ITemplateLiteralLikeNode)span.Literal!).Text;
            if (literalText is not null && literalText.Length > 0)
            {
                args.Add(_factory.CreateStringLiteral(literalText, null, null));
            }

            expression = _factory.CreateCallExpression(
                _factory.CreatePropertyAccessExpression(expression, "concat"),
                /*typeArguments*/ null,
                args);
        }

        return Utilities.SetTextRange(expression, node);
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4823-4828 — <c>createSyntheticSuper</c>.</summary>
    private Identifier CreateSyntheticSuper()
        => _factory.CreateUniqueName("_super", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4830-4838 — <c>visitSuperKeyword</c>.</summary>
    private LeftHandSideExpression VisitSuperKeyword(SuperExpression node, bool isExpressionOfCall)
    {
        LeftHandSideExpression expression = (_hierarchyFacts & HierarchyFacts.NonStaticClassElement) != 0 && !isExpressionOfCall
            ? _factory.CreatePropertyAccessExpression(NodeFactory.SetOriginalNode(CreateSyntheticSuper(), node), "prototype")
            : CreateSyntheticSuper();
        NodeFactory.SetOriginalNode(expression, node);
        EmitNodes.SetCommentRange(expression, node);
        EmitNodes.SetSourceMapRange(expression, TypeScriptTransformSupport.SourceMapRangeOf(node));
        return expression;
    }

    /// <summary>ported from: src/compiler/transformers/es2015.ts:4840-4853 — <c>visitMetaProperty</c>.</summary>
    private Node VisitMetaProperty(MetaProperty node)
    {
        if (node.KeywordToken == SyntaxKind.NewKeyword && node.Name!.EscapedText == "target")
        {
            _hierarchyFacts |= HierarchyFacts.NewTarget;
            return _factory.CreateUniqueName("_newTarget", GeneratedIdentifierFlags.Optimistic | GeneratedIdentifierFlags.FileLevel);
        }
        return node;
    }
}
