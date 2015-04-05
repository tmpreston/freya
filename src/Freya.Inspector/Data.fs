﻿//----------------------------------------------------------------------------
//
// Copyright (c) 2014
//
//    Ryan Riley (@panesofglass) and Andrew Cherry (@kolektiv)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//    http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
//----------------------------------------------------------------------------

[<AutoOpen>]
module internal Freya.Inspector.Data

open System
open Chiron
open Freya.Core
open Freya.Core.Operators
open Freya.Machine
open Freya.Machine.Extensions.Http
open Freya.Machine.Router
open Freya.Recorder
open Freya.Router
open Freya.Types.Uri.Template

(* Route *)

let private id =
    Freya.memo ((Option.get >> Guid.Parse) <!> Freya.getLensPartial (Route.atom "id"))

let private extension =
    Freya.memo ((Option.get) <!> Freya.getLensPartial (Route.atom "ext"))

(* Data *)

let private recordHeaders =
        List.map (fun r ->
            { FreyaRecorderRecordHeader.Id = r.Id
              Timestamp = r.Timestamp }) >> Json.serialize
    <!> listRecords

let private recordDetail =
        Option.map (fun r ->
            { FreyaRecorderRecordDetail.Id = r.Id
              Timestamp = r.Timestamp
              Extensions = (Map.toList >> List.map fst) r.Data } |> Json.serialize)
    <!> (getRecord =<< id)

let private inspectionData inspectors =
        fun record extension ->
            match Map.tryFind extension inspectors with
            | Some inspector -> Option.bind inspector.Inspection.Data record
            | _ -> None
    <!> (getRecord =<< id)
    <*> extension

(* Functions *)

let private recordsGet _ =
    representJSON <!> recordHeaders

let private recordGet _ =
    (Option.get >> representJSON) <!> recordDetail

let private recordExists =
    Option.isSome <!> recordDetail

let private inspectionGet inspectors _=
    (Option.get >> representJSON) <!> (inspectionData inspectors)

let private inspectionExists inspectors =
    Option.isSome <!> (inspectionData inspectors)

(* Resources *)

let private records =
    freyaMachine {
        including defaults
        handleOk recordsGet
        mediaTypesSupported Prelude.json } |> FreyaMachine.toPipeline

let private record =
    freyaMachine {
        including defaults
        exists recordExists
        handleOk recordGet
        mediaTypesSupported Prelude.json } |> FreyaMachine.toPipeline

let private inspection inspectors =
    freyaMachine {
        including defaults
        exists (inspectionExists inspectors)
        handleOk (inspectionGet inspectors)
        mediaTypesSupported Prelude.json } |> FreyaMachine.toPipeline

(* Routes

   Note: More thought should be given to a more expandable API
   path namespacing approach in the near future. *)

let private root =
    UriTemplate.Parse "/freya/inspector/api/records"

let data config =
    let inspectors =
        config.Inspectors
        |> List.map (fun x -> x.Key, x)
        |> Map.ofList
        |> inspection

    freyaRouter {
        resource (root) records
        resource (root + UriTemplate.Parse "/{id}") record
        resource (root + UriTemplate.Parse "/{id}/extensions/{ext}") inspectors } |> FreyaRouter.toPipeline