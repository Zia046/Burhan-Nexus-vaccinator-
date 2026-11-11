namespace BurhanNexus.Vaccinator.Domain;

/// <summary>
/// Starting vaccine database for Pakistan. Loaded once into an empty database; afterwards everything is
/// edited by authorised users in Settings → Vaccine Database / Protocols.
///
/// Sources (each record carries its own reference):
///  - Federal Directorate of Immunization (FDI) Pakistan, routine EPI schedule: Birth BCG, OPV-0, Hep B-0;
///    6/10/14 weeks Pentavalent, PCV, OPV (+ Rota at 6/10, IPV-1 at 14); 9 months MR-1, TCV, IPV-2; 15 months MR-2.
///  - WHO vaccine position papers (Weekly Epidemiological Record), cited per vaccine.
///  - Volumes marked "as per product leaflet" differ between brands: confirm with the leaflet of the product in use.
///
/// No protocol is marked "approved by center": the center's medical officer must review and approve each
/// protocol before relying on it. Until then the software shows a warning whenever the protocol is used.
/// </summary>
public static class VaccineCatalog
{
    public const string EpiGroup = "Pakistan EPI (newborn / infant)";
    private const string FdiRef = "Federal Directorate of Immunization (FDI) Pakistan – routine EPI schedule; WHO EMRO Pakistan EPI programme page.";
    private const string Cold = "+2 °C to +8 °C. Do not freeze.";

    public static List<Vaccine> Build()
    {
        var list = new List<Vaccine>();

        // ------------------------------------------------------------------ EPI vaccines
        list.Add(V("BCG", "BCG", "Bacillus Calmette–Guérin vaccine", "Tuberculosis (severe childhood forms)",
            "At birth", "0.05 mL (infants under 1 year)", AdministrationRoute.ID, "Right upper arm (deltoid)", "At birth",
            null, "+2 °C to +8 °C. Protect from light. Use reconstituted vaccine within the time stated by the manufacturer (normally 6 hours).",
            "Live vaccine. Intradermal technique required.", FdiRef,
            P("EPI – BCG at birth", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "BCG", 0, IntervalUnit.Days, "0.05 mL", AdministrationRoute.ID, "Right upper arm"))));

        list.Add(V("OPV", "OPV", "Oral Polio Vaccine (bivalent)", "Poliomyelitis",
            "Birth, 6, 10 and 14 weeks", "2 drops", AdministrationRoute.Oral, "Mouth", "At birth",
            "Additional doses are given in national polio campaigns (SIAs).", "+2 °C to +8 °C at facility level; −20 °C at store level. Check VVM.",
            "Do not touch the dropper to the child's mouth.", FdiRef,
            P("EPI – OPV 0, 1, 2, 3", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "OPV-0", 0, IntervalUnit.Days, "2 drops", AdministrationRoute.Oral, "Mouth"),
                D(2, "OPV-1", 6, IntervalUnit.Weeks, "2 drops", AdministrationRoute.Oral, "Mouth", 4, IntervalUnit.Weeks),
                D(3, "OPV-2", 10, IntervalUnit.Weeks, "2 drops", AdministrationRoute.Oral, "Mouth", 4, IntervalUnit.Weeks),
                D(4, "OPV-3", 14, IntervalUnit.Weeks, "2 drops", AdministrationRoute.Oral, "Mouth", 4, IntervalUnit.Weeks))));

        list.Add(V("IPV", "IPV", "Inactivated Polio Vaccine", "Poliomyelitis",
            "14 weeks and 9 months", "0.5 mL (IM); fractional IPV 0.1 mL ID where the programme uses it", AdministrationRoute.IM, "Right thigh (anterolateral)", "14 weeks",
            null, Cold, "Check whether full-dose IM or fractional ID IPV is used by your district programme.", FdiRef,
            P("EPI – IPV-1 and IPV-2", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "IPV-1", 14, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Right thigh"),
                D(2, "IPV-2", 9, IntervalUnit.Months, "0.5 mL", AdministrationRoute.IM, "Thigh", 4, IntervalUnit.Weeks))));

        list.Add(V("PENTA", "Pentavalent", "DTwP-HepB-Hib (Pentavalent) vaccine", "Diphtheria, Tetanus, Pertussis, Hepatitis B, Haemophilus influenzae type b",
            "6, 10 and 14 weeks", "0.5 mL", AdministrationRoute.IM, "Left thigh (anterolateral)", "6 weeks",
            null, Cold, "Shake test if freezing is suspected.", FdiRef,
            P("EPI – Pentavalent 1, 2, 3", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "Penta-1", 6, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Left thigh"),
                D(2, "Penta-2", 10, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Left thigh", 4, IntervalUnit.Weeks),
                D(3, "Penta-3", 14, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Left thigh", 4, IntervalUnit.Weeks))));

        list.Add(V("PCV", "PCV", "Pneumococcal Conjugate Vaccine", "Pneumococcal disease (pneumonia, meningitis)",
            "6, 10 and 14 weeks", "0.5 mL", AdministrationRoute.IM, "Right thigh (anterolateral)", "6 weeks",
            null, Cold, null, FdiRef,
            P("EPI – PCV 1, 2, 3", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "PCV-1", 6, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Right thigh"),
                D(2, "PCV-2", 10, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Right thigh", 4, IntervalUnit.Weeks),
                D(3, "PCV-3", 14, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.IM, "Right thigh", 4, IntervalUnit.Weeks))));

        list.Add(V("ROTA", "Rotavirus", "Rotavirus vaccine (oral)", "Rotavirus diarrhoea",
            "6 and 10 weeks", "As per product leaflet (oral)", AdministrationRoute.Oral, "Mouth", "6 weeks",
            null, Cold, "Upper age limits for starting/completing the course depend on the product: check the leaflet.", FdiRef,
            P("EPI – Rota 1, 2", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "Rota-1", 6, IntervalUnit.Weeks, null, AdministrationRoute.Oral, "Mouth"),
                D(2, "Rota-2", 10, IntervalUnit.Weeks, null, AdministrationRoute.Oral, "Mouth", 4, IntervalUnit.Weeks))));

        list.Add(V("MR", "MR", "Measles-Rubella vaccine", "Measles, Rubella",
            "9 and 15 months", "0.5 mL", AdministrationRoute.SC, "Left upper arm", "9 months",
            null, "+2 °C to +8 °C. Protect from light. Discard reconstituted vaccine after 6 hours or at the end of the session.",
            "Live vaccine.", FdiRef,
            P("EPI – MR-1 and MR-2", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "MR-1", 9, IntervalUnit.Months, "0.5 mL", AdministrationRoute.SC, "Left upper arm"),
                D(2, "MR-2", 15, IntervalUnit.Months, "0.5 mL", AdministrationRoute.SC, "Left upper arm", 4, IntervalUnit.Weeks))));

        list.Add(V("MEASLES", "Measles", "Measles vaccine (monovalent)", "Measles",
            "9 and 15 months (where MR is not used)", "0.5 mL", AdministrationRoute.SC, "Left upper arm", "9 months",
            null, "+2 °C to +8 °C. Protect from light.", "Pakistan EPI now uses MR; keep for older records.",
            "WHO Measles vaccines position paper, WER 2017;92(17).",
            P("Measles 2-dose", "Children", ProtocolKind.Routine, ScheduleAnchor.DateOfBirth, "WHO Measles vaccines position paper, WER 2017;92(17).",
                D(1, "Measles-1", 9, IntervalUnit.Months, "0.5 mL", AdministrationRoute.SC, "Left upper arm"),
                D(2, "Measles-2", 15, IntervalUnit.Months, "0.5 mL", AdministrationRoute.SC, "Left upper arm", 4, IntervalUnit.Weeks))));

        list.Add(V("TCV", "Typhoid (TCV)", "Typhoid Conjugate Vaccine", "Typhoid fever",
            "9 months (EPI); single dose for older children and adults", "0.5 mL", AdministrationRoute.IM, "Thigh (infants) / deltoid (older)", "6 months",
            null, Cold, null, FdiRef + " WHO Typhoid vaccines position paper, WER 2018;93(13).",
            P("EPI – TCV at 9 months", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef,
                D(1, "TCV", 9, IntervalUnit.Months, "0.5 mL", AdministrationRoute.IM, "Right thigh")),
            P("TCV single dose (catch-up / adult)", "Children over 9 months and adults", ProtocolKind.Routine, ScheduleAnchor.FirstDose,
                "WHO Typhoid vaccines position paper, WER 2018;93(13).",
                D(1, "TCV", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid"))));

        // ------------------------------------------------------------------ Hepatitis B: different protocols per group
        const string hepBRef = "WHO Hepatitis B vaccines position paper, WER 2017;92(27).";
        list.Add(V("HEPB", "Hepatitis B", "Hepatitis B vaccine", "Hepatitis B",
            "Birth dose (newborn); 3 doses for unvaccinated children/adults", "Paediatric 0.5 mL / adult 1.0 mL (as per product leaflet)",
            AdministrationRoute.IM, "Thigh (infants) / deltoid (adults)", "At birth",
            "Routine booster not recommended for healthy persons who completed the series.", Cold, "Birth dose ideally within 24 hours of birth.",
            FdiRef + " " + hepBRef,
            P("EPI – Hep B birth dose", EpiGroup, ProtocolKind.ChildhoodEpi, ScheduleAnchor.DateOfBirth, FdiRef + " " + hepBRef,
                D(1, "HepB-0", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Thigh")),
            P("Hep B 0-1-6 months (children / adults)", "Unvaccinated children and adults", ProtocolKind.Routine, ScheduleAnchor.FirstDose, hepBRef,
                D(1, "Dose 1", 0, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid"),
                D(2, "Dose 2", 1, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid", 4, IntervalUnit.Weeks),
                D(3, "Dose 3", 6, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid", 8, IntervalUnit.Weeks)),
            P("Hep B accelerated 0-1-2-12 months", "Rapid protection needed (as advised)", ProtocolKind.Routine, ScheduleAnchor.FirstDose,
                hepBRef + " Product leaflet for accelerated schedule.",
                D(1, "Dose 1", 0, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid"),
                D(2, "Dose 2", 1, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid", 4, IntervalUnit.Weeks),
                D(3, "Dose 3", 2, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid", 4, IntervalUnit.Weeks),
                D(4, "Booster", 12, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid", booster: true))));

        const string hepARef = "WHO Hepatitis A vaccines position paper, WER 2022;97(40). Product leaflet.";
        list.Add(V("HEPA", "Hepatitis A", "Hepatitis A vaccine (inactivated)", "Hepatitis A",
            "From 12 months", "As per product leaflet (paediatric / adult)", AdministrationRoute.IM, "Deltoid / thigh", "12 months",
            null, Cold, null, hepARef,
            P("Hep A inactivated 2-dose (0, 6 months)", "Children from 12 months and adults", ProtocolKind.Routine, ScheduleAnchor.FirstDose, hepARef,
                D(1, "Dose 1", 0, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid"),
                D(2, "Dose 2", 6, IntervalUnit.Months, null, AdministrationRoute.IM, "Deltoid"))));

        const string tdRef = "WHO Tetanus vaccines position paper, WER 2017;92(6). Pakistan EPI Td schedule for women of child-bearing age.";
        list.Add(V("TD", "Tetanus / Td", "Tetanus-diphtheria (Td) vaccine", "Tetanus (incl. maternal/neonatal), Diphtheria",
            "Women of child-bearing age, pregnant women, wound management", "0.5 mL", AdministrationRoute.IM, "Left upper arm (deltoid)", "7 years (Td)",
            "5 doses give long-term protection.", Cold, "Shake test if freezing is suspected.", tdRef,
            P("Td 5-dose (women of child-bearing age)", "Women 15–49 years / pregnant women", ProtocolKind.Routine, ScheduleAnchor.FirstDose, tdRef,
                D(1, "Td-1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid"),
                D(2, "Td-2", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid", 4, IntervalUnit.Weeks),
                D(3, "Td-3", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid", 6, IntervalUnit.Months),
                D(4, "Td-4", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid", 1, IntervalUnit.Years),
                D(5, "Td-5", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid", 1, IntervalUnit.Years))));

        const string hpvRef = "WHO HPV vaccines position paper, WER 2022;97(50) (single-dose and 2-dose options). Pakistan HPV introduction (girls 9–14 years).";
        list.Add(V("HPV", "HPV", "Human Papillomavirus vaccine", "Cervical cancer and other HPV-related disease",
            "Girls 9–14 years (primary target)", "0.5 mL", AdministrationRoute.IM, "Deltoid", "9 years",
            null, Cold, null, hpvRef,
            P("HPV single dose (girls 9–14)", "Girls 9–14 years", ProtocolKind.Routine, ScheduleAnchor.FirstDose, hpvRef,
                D(1, "HPV", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid")),
            P("HPV 2-dose (0, 6 months)", "9–20 years (where 2 doses are used)", ProtocolKind.Routine, ScheduleAnchor.FirstDose, hpvRef,
                D(1, "Dose 1", 0, IntervalUnit.Months, "0.5 mL", AdministrationRoute.IM, "Deltoid"),
                D(2, "Dose 2", 6, IntervalUnit.Months, "0.5 mL", AdministrationRoute.IM, "Deltoid"))));

        const string fluRef = "WHO Influenza vaccines position paper, WER 2022;97(19). Product leaflet for the current season.";
        list.Add(V("FLU", "Influenza", "Seasonal influenza vaccine (inactivated)", "Seasonal influenza",
            "Annually; priority groups (pregnant women, elderly, chronic illness, health workers)", "0.5 mL (as per product leaflet)",
            AdministrationRoute.IM, "Deltoid", "6 months",
            "Repeat every season with the current season's vaccine. Children under 9 receiving it for the first time may need 2 doses (as per leaflet).",
            Cold, null, fluRef,
            P("Influenza – annual dose", "Adults and children 9+ years", ProtocolKind.Routine, ScheduleAnchor.FirstDose, fluRef,
                D(1, "This season", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid"),
                D(2, "Next season", 1, IntervalUnit.Years, "0.5 mL", AdministrationRoute.IM, "Deltoid", booster: true))));

        const string mmrRef = "WHO Measles and Rubella vaccines position papers (WER 2017;92(17), WER 2020;95(27)). Product leaflet.";
        list.Add(V("MMR", "MMR", "Measles-Mumps-Rubella vaccine", "Measles, Mumps, Rubella",
            "Private schedule / catch-up", "0.5 mL", AdministrationRoute.SC, "Upper arm", "9–12 months (as per leaflet)",
            null, "+2 °C to +8 °C. Protect from light.", "Live vaccine: not in pregnancy.", mmrRef,
            P("MMR 2-dose catch-up (min 4 weeks)", "Children and adults (catch-up)", ProtocolKind.Routine, ScheduleAnchor.FirstDose, mmrRef,
                D(1, "Dose 1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.SC, "Upper arm"),
                D(2, "Dose 2", 4, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.SC, "Upper arm"))));

        const string varRef = "WHO Varicella and herpes zoster vaccines position paper, WER 2014;89(25). Product leaflet.";
        list.Add(V("VAR", "Varicella", "Varicella (chickenpox) vaccine", "Chickenpox",
            "From 12 months", "0.5 mL", AdministrationRoute.SC, "Upper arm", "12 months",
            null, "As per product leaflet (some products need frozen storage).", "Live vaccine: not in pregnancy.", varRef,
            P("Varicella 2-dose 13+ years (0, 4 weeks)", "Adolescents 13+ and adults", ProtocolKind.Routine, ScheduleAnchor.FirstDose, varRef,
                D(1, "Dose 1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.SC, "Upper arm"),
                D(2, "Dose 2", 4, IntervalUnit.Weeks, "0.5 mL", AdministrationRoute.SC, "Upper arm")),
            P("Varicella 2-dose children (0, 3 months)", "Children 12 months – 12 years", ProtocolKind.Routine, ScheduleAnchor.FirstDose, varRef,
                D(1, "Dose 1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.SC, "Upper arm"),
                D(2, "Dose 2", 3, IntervalUnit.Months, "0.5 mL", AdministrationRoute.SC, "Upper arm"))));

        const string menRef = "WHO Meningococcal vaccines position paper, WER 2011;86(47). Product leaflet (e.g. Hajj/Umrah requirement).";
        list.Add(V("MEN", "Meningococcal", "Meningococcal conjugate vaccine (MenACWY)", "Meningococcal meningitis",
            "Travellers (Hajj/Umrah), high-risk groups", "0.5 mL", AdministrationRoute.IM, "Deltoid", "As per product leaflet",
            "Booster interval depends on product and risk.", Cold, null, menRef,
            P("MenACWY single dose", "Adolescents, adults, travellers", ProtocolKind.Routine, ScheduleAnchor.FirstDose, menRef,
                D(1, "Dose 1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid"))));

        const string ppsvRef = "WHO Pneumococcal vaccines position paper, WER 2019;94(8). Product leaflet.";
        list.Add(V("PPSV23", "Pneumococcal (adult)", "Pneumococcal polysaccharide vaccine (PPSV23)", "Pneumococcal disease (adults)",
            "Elderly and high-risk adults (as advised)", "0.5 mL", AdministrationRoute.IM, "Deltoid", "2 years",
            null, Cold, "Use only on medical advice.", ppsvRef,
            P("PPSV23 single dose", "Adults on medical advice", ProtocolKind.Routine, ScheduleAnchor.FirstDose, ppsvRef,
                D(1, "Dose 1", 0, IntervalUnit.Days, "0.5 mL", AdministrationRoute.IM, "Deltoid"))));

        // ------------------------------------------------------------------ Rabies
        const string rabRef = "WHO Rabies vaccines position paper, WER 2018;93(16):201–220; WHO Expert Consultation on Rabies, 3rd report (TRS 1012, 2018). Product leaflet.";
        const string rabWarn = "Wash the wound immediately for 15 minutes with soap and water. Category III exposure (and category II in immunocompromised patients) needs professional assessment for rabies immunoglobulin (RIG). The software does not decide exposure category or treatment.";
        var rabies = V("RABIES", "Rabies", "Rabies vaccine (cell-culture)", "Rabies",
            "Any age after exposure (PEP) or before exposure (PrEP)", "IM: full dose as per product leaflet; ID: 0.1 mL per site",
            AdministrationRoute.IM, "Deltoid (adults) / anterolateral thigh (small children). Never in the gluteal region.", "Any age",
            "Previously vaccinated persons need a shortened booster course without RIG.", Cold,
            "If a dose is delayed, continue the schedule – do not restart.", rabRef);

        rabies.Protocols.Add(P("PEP IM 1-site – days 0, 3, 7, 14 (4 visits)", "Not previously vaccinated", ProtocolKind.RabiesPepIntramuscular,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0", 0, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(2, "Day 3", 3, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(3, "Day 7", 7, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(4, "Day 14", 14, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));
        rabies.Protocols.Add(P("PEP IM 1-site – days 0, 3, 7, 28 (4th dose at day 28)", "Not previously vaccinated (product-specific)", ProtocolKind.RabiesPepIntramuscular,
            ScheduleAnchor.FirstDose, rabRef + " WHO allows the 4th dose between day 14 and 28.",
            D(1, "Day 0", 0, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(2, "Day 3", 3, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(3, "Day 7", 7, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(4, "Day 28", 28, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));
        rabies.Protocols.Add(P("PEP IM Zagreb 2-1-1 – days 0 (2 sites), 7, 21", "Not previously vaccinated", ProtocolKind.RabiesPepIntramuscular,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0 (2 doses, both arms)", 0, IntervalUnit.Days, "2 × IM dose", AdministrationRoute.IM, "Left and right deltoid"),
            D(2, "Day 7", 7, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(3, "Day 21", 21, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));
        rabies.Protocols.Add(P("PEP ID 2-site – days 0, 3, 7 (IPC)", "Not previously vaccinated", ProtocolKind.RabiesPepIntradermal,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0 (2 ID sites)", 0, IntervalUnit.Days, "0.1 mL × 2 sites", AdministrationRoute.ID, "Both upper arms"),
            D(2, "Day 3 (2 ID sites)", 3, IntervalUnit.Days, "0.1 mL × 2 sites", AdministrationRoute.ID, "Both upper arms"),
            D(3, "Day 7 (2 ID sites)", 7, IntervalUnit.Days, "0.1 mL × 2 sites", AdministrationRoute.ID, "Both upper arms")));
        rabies.Protocols.Add(P("Previously vaccinated – IM 1-site days 0, 3", "Documented previous PrEP or PEP", ProtocolKind.RabiesPepPreviouslyVaccinated,
            ScheduleAnchor.FirstDose, rabRef + " RIG is not indicated for previously vaccinated persons.",
            D(1, "Day 0", 0, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(2, "Day 3", 3, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));
        rabies.Protocols.Add(P("Previously vaccinated – ID 1-site days 0, 3", "Documented previous PrEP or PEP", ProtocolKind.RabiesPepPreviouslyVaccinated,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0", 0, IntervalUnit.Days, "0.1 mL", AdministrationRoute.ID, "Upper arm"),
            D(2, "Day 3", 3, IntervalUnit.Days, "0.1 mL", AdministrationRoute.ID, "Upper arm")));
        rabies.Protocols.Add(P("Previously vaccinated – ID 4-site, day 0 only", "Documented previous PrEP or PEP", ProtocolKind.RabiesPepPreviouslyVaccinated,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0 (4 ID sites)", 0, IntervalUnit.Days, "0.1 mL × 4 sites", AdministrationRoute.ID, "Both deltoids and thighs/suprascapular")));
        rabies.Protocols.Add(P("Pre-exposure (PrEP) – IM days 0, 7", "High-risk persons (vets, animal handlers, lab staff)", ProtocolKind.RabiesPreExposure,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0", 0, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(2, "Day 7", 7, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));
        rabies.Protocols.Add(P("Pre-exposure (PrEP) – ID 2-site days 0, 7", "High-risk persons", ProtocolKind.RabiesPreExposure,
            ScheduleAnchor.FirstDose, rabRef,
            D(1, "Day 0 (2 ID sites)", 0, IntervalUnit.Days, "0.1 mL × 2 sites", AdministrationRoute.ID, "Both upper arms"),
            D(2, "Day 7 (2 ID sites)", 7, IntervalUnit.Days, "0.1 mL × 2 sites", AdministrationRoute.ID, "Both upper arms")));
        rabies.Protocols.Add(P("Immunocompromised – IM days 0, 3, 7, 14, 28 (clinical review)", "Immunocompromised patients – only on professional advice",
            ProtocolKind.RabiesSpecialCategory, ScheduleAnchor.FirstDose,
            rabRef + " Immunocompromised patients need professional assessment (RIG for category II and III, antibody testing where available).",
            D(1, "Day 0", 0, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(2, "Day 3", 3, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(3, "Day 7", 7, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(4, "Day 14", 14, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid"),
            D(5, "Day 28", 28, IntervalUnit.Days, null, AdministrationRoute.IM, "Deltoid")));

        foreach (var p in rabies.Protocols)
        {
            p.Warnings = rabWarn;
            p.CatchUpRule = "Delayed dose: give it as soon as possible and continue; following doses keep their intervals. Do not restart the course.";
            p.ReviewIfLateByDays = 7;
        }
        list.Add(rabies);

        // Defaults for every protocol
        var order = 0;
        foreach (var v in list)
            foreach (var p in v.Protocols)
            {
                p.SortOrder = order++;
                p.ApprovedByCenter = false;
                p.CatchUpRule ??= p.Kind == ProtocolKind.ChildhoodEpi
                    ? "Late dose: give at the next visit while respecting the minimum 4-week interval. Do not restart the series. Check upper age limits (e.g. Rota) with the product leaflet."
                    : "Late dose: give as soon as possible; minimum intervals are kept. Do not restart the series.";
                p.ReviewIfLateByDays ??= p.Kind == ProtocolKind.ChildhoodEpi ? 90 : 60;
            }

        return list;
    }

    private static Vaccine V(string code, string name, string full, string disease, string age, string? volume,
        AdministrationRoute route, string site, string minAge, string? booster, string storage, string? special, string reference,
        params VaccineProtocol[] protocols)
    {
        var v = new Vaccine
        {
            Code = code, Name = name, FullName = full, DiseasePrevented = disease, RecommendedAge = age, DoseVolume = volume,
            Route = route, InjectionSite = site, MinimumAge = minAge, BoosterRequirements = booster, StorageTemperature = storage,
            SpecialInstructions = special, Reference = reference, LastUpdated = new DateOnly(2026, 11, 11)
        };
        v.Protocols.AddRange(protocols);
        return v;
    }

    private static VaccineProtocol P(string name, string group, ProtocolKind kind, ScheduleAnchor anchor, string reference, params ProtocolDose[] doses)
    {
        var p = new VaccineProtocol { Name = name, TargetGroup = group, Kind = kind, Anchor = anchor, Reference = reference };
        p.Doses.AddRange(doses);
        return p;
    }

    private static ProtocolDose D(int number, string label, int offset, IntervalUnit unit, string? volume, AdministrationRoute route, string site,
        int? minValue = null, IntervalUnit? minUnit = null, bool booster = false) => new()
    {
        DoseNumber = number, Label = label, OffsetValue = offset, OffsetUnit = unit, DoseVolume = volume, Route = route, InjectionSite = site,
        MinIntervalValue = minValue, MinIntervalUnit = minUnit, IsBooster = booster
    };
}
