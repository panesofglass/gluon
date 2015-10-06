﻿// Copyright 2015 Tachyus Corp.
//
// Licensed under the Apache License, Version 2.0 (the "License"); you
// may not use this file except in compliance with the License. You may
// obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or
// implied. See the License for the specific language governing
// permissions and limitations under the License.

/// Converts Gluon Entities into Syntax trees.
module internal Tachyus.Gluon.TypeScript.CodeGen

open Tachyus.Gluon

val methodStubs : Schema.Service -> Syntax.Definitions
val typeDefinitions : seq<Schema.TypeDefinition> -> Syntax.Definitions
val registerActivators : seq<Schema.TypeDefinition> -> Syntax.Definitions
val registerService : Schema.Service -> Syntax.Definitions
val registerTypeDefinitions : seq<Schema.TypeDefinition> -> Syntax.Definitions
